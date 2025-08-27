using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Configuration;
using Worker.Data.Entities;
using Worker.Infrastructure;

namespace Worker.Services
{
    public class MetadataJobProcessor : IMetadataJobProcessor
    {
        private readonly BlobServiceClient _bsc;
        private readonly IConfiguration _cfg;
        private readonly IDocumentRepository _repo;
        private readonly ResiliencePolicyFactory _policies;
        private readonly IZipExtractor _extractor;
        private readonly JobLogger _jobLogger;
        //private readonly ILogger<MetadataJobProcessor> _logger;

        public MetadataJobProcessor(BlobServiceClient bsc, IConfiguration cfg, IDocumentRepository repo,
            ResiliencePolicyFactory policies, IZipExtractor extractor, JobLogger jobLogger, ILogger<MetadataJobProcessor> logger)
        {
            _bsc = bsc; _cfg = cfg; _repo = repo; _policies = policies; _extractor = extractor; _jobLogger = jobLogger;
        }

        public async Task RunAsync(Guid jobId, string stagedZipBlobName, string country, string appName, CancellationToken ct)
        {
            //await _jobLogger.MarkJobStartedAsync(jobId);
            await _repo.EnsureJobStartedAsync(jobId);

            var stage = _bsc.GetBlobContainerClient(_cfg["Storage:StageContainer"]);
            var prod = _bsc.GetBlobContainerClient(_cfg["Storage:ProdContainer"]);
            await _policies.BlobRetryPolicy.ExecuteAsync(async () => await prod.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct));

            var zipBlob = stage.GetBlobClient(stagedZipBlobName);
            var correlationId = stagedZipBlobName.Split('/')[0];

            await _extractor.ExtractToContainerAsync(zipBlob, stage, correlationId, ct);

            var metadataBlob = stage.GetBlobClient($"{correlationId}/metadata.json");
            if (!(await _policies.BlobRetryPolicy.ExecuteAsync(async () => await metadataBlob.ExistsAsync(ct))).Value)
            {
                await _repo.MarkJobFailedAsync(jobId, "metadata.json missing");
                //await _jobLogger.MarkJobFailedAsync(jobId, "metadata.json not found after extraction");
                //throw new InvalidOperationException("metadata.json not found after extraction");
                return;
            }

            await new MetadataStager(_repo, _jobLogger).StageMetadataAsync(jobId, metadataBlob, ct);

            MetadataDto metadata;
            using (var ms = new MemoryStream())
            {
                using var rs = await metadataBlob.OpenReadAsync(cancellationToken: ct);
                await rs.CopyToAsync(ms, ct);
                ms.Position = 0;
                metadata = JsonSerializer.Deserialize<MetadataDto>(ms.ToArray())!;
            }
            var tagMap = BuildTagLookup(metadata);

            var maxDoP = int.TryParse(_cfg["Processing:MaxDegreeOfParallelism"], out var dop) ? dop : 16;
            var batchSize = int.TryParse(_cfg["Processing:DbBatchSize"], out var bs) ? bs : 500;

            var skip = 0;
            while (true)
            {
                var (batch, _) = await _repo.GetPendingByJobBatchAsync(jobId, skip, batchSize, ct);
                if (batch.Count == 0) break;

                await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = maxDoP, CancellationToken = ct }, async (doc, token) =>
                {
                    try
                    {
                        var src = stage.GetBlobClient($"{correlationId}/{doc.Filepath}");
                        if (!(await _policies.BlobRetryPolicy.ExecuteAsync(async () => await src.ExistsAsync(token))).Value)
                            throw new FileNotFoundException($"Missing staged blob: {src.Name}");

                        var dest = prod.GetBlobClient(doc.Filepath);
                        await _policies.BlobRetryPolicy.ExecuteAsync(async () =>
                        {
                            await using var s = await src.OpenReadAsync(cancellationToken: token);
                            await dest.UploadAsync(s, overwrite: true, cancellationToken: token);
                        });

                        var tags = tagMap.TryGetValue(Norm(doc.Filepath), out var t) ? t : new Dictionary<string, string>();
                        if (tags.Count > 0)
                            await _policies.BlobRetryPolicy.ExecuteAsync(async () => await dest.SetTagsAsync(tags, cancellationToken: token));

                        await _repo.AddProductionRecordWithTagsAsync(jobId, doc.FileGuid, dest.Name, doc.Extension, tags, token);

                       // await _jobLogger.LogDocumentResultAsync(jobId, doc.Id, true, null);
                        await _jobLogger.LogDocumentCompletionAsync(jobId, doc.Id, true, null, token);
                    }
                    catch (Exception ex)
                    {
                        //await _jobLogger.LogDocumentResultAsync(jobId, doc.Id, false, ex.Message);
                        await _jobLogger.LogDocumentCompletionAsync(jobId, doc.Id, false, ex.Message, token);
                    }
                });

                skip += batch.Count;
                if (batch.Count < batchSize) break;
            }

            //await _jobLogger.MarkJobCompletedAsync(jobId);
            //_logger.LogInformation("Job {JobId} completed", jobId);
            await _repo.MarkJobCompletedAsync(jobId);
            await _jobLogger.LogJobCompletionAsync(jobId, true, null, ct);
        }

        private static string Norm(string p) => p.Replace("\\", "/");

        private static Dictionary<string, Dictionary<string, string>> BuildTagLookup(MetadataDto meta)
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var dt in meta.doctypes)
            {
                foreach (var d in dt.documents)
                {
                    var k = Norm(d.filepath);
                    if (!map.TryGetValue(k, out var dict))
                    {
                        dict = new(StringComparer.OrdinalIgnoreCase);
                        map[k] = dict;
                    }
                    if (d.tags != null)
                        foreach (var obj in d.tags)
                            foreach (var kv in obj)
                                dict[kv.Key] = kv.Value;
                }
            }
            return map;
        }
    }
}
