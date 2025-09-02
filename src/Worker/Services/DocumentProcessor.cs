using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Configuration;
using Worker.Data.Entities;
using Worker.Data.Entities.production;
using Worker.Data.Entities.staging;
using Worker.Data.Repositories;
using Worker.Infrastructure;

namespace Worker.Services
{
    public class DocumentProcessor : IDocumentProcessor
    {
        private readonly IStagingRepository _stgrepo;
        private readonly IProductionRepository _prodrepo;
        private readonly BlobServiceClient _blobService;
        private readonly ResiliencePolicyFactory _policies;
        private readonly ProcessingOptions _opts;
        private readonly ILogger<DocumentProcessor> _logger;
        private readonly IJobAlertService _alerts;

        public DocumentProcessor(IStagingRepository stgrepo, IProductionRepository prodrepo, BlobServiceClient blobService, ResiliencePolicyFactory policies, IOptions<ProcessingOptions> opts, IJobAlertService alerts, ILogger<DocumentProcessor> logger)
        {
            _stgrepo = stgrepo;
            _prodrepo = prodrepo;
            _blobService = blobService;
            _policies = policies;
            _opts = opts.Value;
            _logger = logger;
            _alerts = alerts;
        }

        public async Task ProcessJobAsync(Guid jobId, CancellationToken ct = default)
        {
            var batch = _opts.DbBatchSize > 0 ? _opts.DbBatchSize : 1000;
            var dop = Math.Max(1, _opts.MaxDegreeOfParallelism);

            int totalSuccess = 0, totalFailed = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var pending = await _stgrepo.GetPendingStagingRowsAsync(jobId, batch, ct);
                if (pending == null || pending.Count == 0) break;

                _logger.LogInformation("Processing batch of {Count} staging rows for job {JobId} (DOP={DOP})", pending.Count, jobId, dop);

                await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct }, async (row, token) =>
                {
                    try
                    {
                        // Parse CSV row: expecting exact 11 columns
                        var parts = SplitCsvRow(row.RawData, 11);
                        var country = parts.ElementAtOrDefault(0) ?? "";
                        var doctype = parts.ElementAtOrDefault(1) ?? "";
                        var filepath = parts.ElementAtOrDefault(2) ?? "";
                        var filename = parts.ElementAtOrDefault(3) ?? "";
                        var filedesc = parts.ElementAtOrDefault(4) ?? "";
                        var fileguid = parts.ElementAtOrDefault(5) ?? Guid.NewGuid().ToString();
                        var extension = parts.ElementAtOrDefault(6) ?? "";
                        var operationType = parts.ElementAtOrDefault(7) ?? "";
                        var metadataOnly = parts.ElementAtOrDefault(8) ?? "FALSE";
                        var sensitivity = parts.ElementAtOrDefault(9) ?? "";
                        var tagColumn = parts.ElementAtOrDefault(10) ?? "";

                        // construct source and target blob clients
                        var cfgStage = _blobService.GetBlobContainerClient(Environment.GetEnvironmentVariable("STAGE_CONTAINER") ?? "stage");
                        var cfgProd = _blobService.GetBlobContainerClient(Environment.GetEnvironmentVariable("PROD_CONTAINER") ?? "prod");

                        var sourceBlobClient = cfgStage.GetBlobClient(filepath);
                        var exists = await _policies.BlobRetryPolicy.ExecuteAsync(async t => (await sourceBlobClient.ExistsAsync(cancellationToken: t)).Value, token);
                        if (!exists)
                        {
                            throw new InvalidOperationException($"Source blob not found: {filepath}");
                        }

                        // destination blob path: keep same relative path or use filename
                        var destBlobName = filepath; // you may choose another naming scheme
                        var destClient = cfgProd.GetBlobClient(destBlobName);

                        // Start server-side copy (fast, no pod streaming)
                        var copyOp = await _policies.BlobRetryPolicy.ExecuteAsync(async t =>
                        {
                            var uri = sourceBlobClient.Uri;
                            var op = await destClient.StartCopyFromUriAsync(uri, cancellationToken: t);
                            return op;
                        }, token);

                        // Wait for copy completion (poll)
                        var copyStatus = await WaitForCopyCompletionAsync(destClient, copyOp.Id, token);

                        if (copyStatus != CopyStatus.Success)
                        {
                            throw new InvalidOperationException($"Copy failed for {filepath} status={copyStatus}");
                        }

                        // set tags parsed from tagColumn (format: key1:val1|key2:val2)
                        var tags = ParseTags(tagColumn);
                        if (tags != null && tags.Any())
                        {
                            await _policies.BlobRetryPolicy.ExecuteAsync(async t => { await destClient.SetTagsAsync(tags, cancellationToken: t); return true; }, token);
                        }

                        // Save production record & tags to DB (idempotent inside repo)
                        var prodEntity = new ProductionDocumentEntity
                        {
                            Id = Guid.NewGuid(),
                            JobId = row.JobId,
                            FileGuid = fileguid,
                            FileName = filename,                            
                            Extension = extension,
                            BlobUrl = destClient.Uri.ToString(),
                            CreatedAt = DateTimeOffset.UtcNow
                        };

                        var prodTags = tags?.Select(kv => new ProductionDocumentTag { Id = Guid.NewGuid(), ProductionDocumentId = prodEntity.Id, Key = kv.Key, Value = kv.Value }).ToList();

                        await _prodrepo.SaveProductionDocumentAsync(prodEntity, prodTags, token);

                        await _stgrepo.UpdateStagingRowStatusAsync(row.Id, nameof(StagingRowStatus.Succeeded), null, token);

                        Interlocked.Increment(ref totalSuccess);
                    }
                    catch (Exception ex)
                    {
                        _alerts.TrackDocumentFailure(jobId, row.RawData ?? "<raw>", ex.Message);
                        _logger.LogError(ex, "Failed processing staging row {StagingId}", row.Id);
                        try { await _stgrepo.UpdateStagingRowStatusAsync(row.Id, nameof(StagingRowStatus.Failed), ex.Message, token); } catch { /* swallow */ }
                        Interlocked.Increment(ref totalFailed);
                    }
                });
            }

            _logger.LogInformation("Job {JobId} processing finished: success={Success}, failed={Failed}", jobId, totalSuccess, totalFailed);
            
            // update job summary
            await _stgrepo.SetJobCompletedAsync(jobId, totalSuccess, totalFailed, totalFailed > 0 ? "Some failures" : null, ct);
            _alerts.TrackCustomMetric("JobProcessed.SuccessCount", totalSuccess, jobId);
            _alerts.TrackCustomMetric("JobProcessed.FailedCount", totalFailed, jobId);
        }

        private static Dictionary<string, string> ParseTags(string tagColumn)
        {
            if (string.IsNullOrWhiteSpace(tagColumn)) return new Dictionary<string, string>();
            var pairs = tagColumn.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pairs)
            {
                var kv = p.Split(':', 2);
                if (kv.Length == 2)
                {
                    dict[kv[0].Trim()] = kv[1].Trim();
                }
            }
            return dict;
        }

        // Wait for copy completion - polls blob properties CopyStatus until not Pending
        private async Task<CopyStatus> WaitForCopyCompletionAsync(BlobClient destClient, string copyId, CancellationToken ct)
        {
            var start = DateTime.UtcNow;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var props = await destClient.GetPropertiesAsync(cancellationToken: ct);
                var copyStatus = props.Value.CopyStatus;
                if (copyStatus != CopyStatus.Pending) return copyStatus;
                if (DateTime.UtcNow - start > TimeSpan.FromMinutes(15)) // copy timeout threshold
                {
                    throw new TimeoutException($"Copy operation timed out for copyId={copyId}");
                }
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        // Simple CSV split that respects quoted commas lightly (for large-scale use, prefer CsvHelper)
        private static string[] SplitCsvRow(string raw, int expected)
        {
            // Very small CSV parser: split on comma, allow quotes
            var list = new List<string>(expected);
            bool inQuotes = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (ch == ',' && !inQuotes) { list.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(ch);
            }
            list.Add(cur.ToString());
            // pad if less
            while (list.Count < expected) list.Add(string.Empty);
            return list.ToArray();
        }
    }
}
