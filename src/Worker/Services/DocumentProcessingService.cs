using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Worker.Configuration;
using Worker.Data;
using Worker.Data.Repositories;
using Worker.Resilience;

namespace Worker.Services
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly IDocumentRepository _repo;
        private readonly IBlobMover _mover;
        private readonly BlobServiceClient _bsc;
        private readonly int _dop;
        private readonly string _stageContainer;
        private readonly string _prodContainer;
        private readonly ILogger<DocumentProcessingService> _log;
        private readonly ResiliencePolicyFactory _policies;

        public DocumentProcessingService(IDocumentRepository repo, IBlobMover mover, IAzureClientFactory fac,
            IOptions<DocumentProcessingOptions> opts, ILogger<DocumentProcessingService> log, ResiliencePolicyFactory policies)
        {
            _repo = repo;
            _mover = mover;
            _bsc = fac.CreateBlobServiceClient();
            var v = opts.Value;
            _dop = Math.Max(1, v.MaxDegreeOfParallelism);
            _stageContainer = v.StageContainer;
            _prodContainer = v.ProdContainer;
            _log = log;
            _policies = policies;
        }

        public async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
        {
            _log.LogInformation("Starting processing for job {Job} with DOP={DOP}", jobId, _dop);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var docs = await _repo.GetPendingDocumentsAsync(jobId, 1000, ct);
                if (docs == null || docs.Count == 0) break;

                await Parallel.ForEachAsync(docs, new ParallelOptions { MaxDegreeOfParallelism = _dop, CancellationToken = ct }, async (d, token) =>
                {
                    try
                    {
                        await _repo.MarkProcessingAsync(d.Id, token);
                        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["country"] = d.Country ?? string.Empty,
                            ["appname"] = d.AppName ?? string.Empty,
                            ["doctype"] = d.DocType ?? string.Empty,
                            ["fileguid"] = d.FileGuid ?? string.Empty,
                            ["jobId"] = jobId.ToString()
                        };

                        await _mover.MoveWithTagsAsync(_stageContainer, _prodContainer, d.Filepath, tags, token);
                        await _repo.MarkSuccessAsync(d.Id, token);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Document processing failed for {Path}", d.Filepath);
                        try { await _repo.MarkFailedAsync(d.Id, ex.Message, token); } catch { _log.LogWarning("Failed to mark failure in DB"); }
                    }
                });
            }

            await _repo.SetJobStatusAsync(jobId, "Completed", ct);
            _log.LogInformation("Completed processing for job {Job}", jobId);
        }
    }
}
