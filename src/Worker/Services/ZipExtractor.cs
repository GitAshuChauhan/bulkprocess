//using Azure.Storage.Blobs;
//using System.IO.Compression;
//using System.Threading;
//using System.Threading.Tasks;
//using Worker.Abstractions;
//using Worker.Infrastructure;

//namespace Worker.Services
//{
//    public class ZipExtractor : IZipExtractor
//    {
//        private readonly ResiliencePolicyFactory _policies;
//        private readonly ILogger<ZipExtractor> _logger;
//        private readonly JobLogger _jobLogger;

//        public ZipExtractor(ResiliencePolicyFactory policies, ILogger<ZipExtractor> logger, JobLogger jobLogger)
//        {
//            _policies = policies; _logger = logger; _jobLogger = jobLogger;
//        }

//        public async Task ExtractToContainerAsync(BlobClient zipBlob, BlobContainerClient stageContainer, string correlationId, CancellationToken ct)
//        {
//            _logger.LogInformation("Extracting zip {Zip} for correlation {Correlation}", zipBlob.Name, correlationId);
//            if (!Guid.TryParse(correlationId, out var g))
//            {
//                _logger.LogWarning("CorrelationId is not a GUID: {C}", correlationId);
//            }
//            else
//            {
//                await _jobLogger.LogInfoAsync(g, "Start extracting zip {Zip}", zipBlob.Name);
//            }

//            using var stream = await _policies.BlobRetryPolicy.ExecuteAsync(async () => await zipBlob.OpenReadAsync(cancellationToken: ct));
//            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

//            foreach (var entry in archive.Entries)
//            {
//                if (string.IsNullOrEmpty(entry.Name)) continue;
//                var destName = $"{correlationId}/{entry.FullName.Replace("\\\\", "/")}";
//                var stageBlob = stageContainer.GetBlobClient(destName);
//                await _policies.BlobRetryPolicy.ExecuteAsync(async () =>
//                {
//                    await using var es = entry.Open();
//                    await stageBlob.UploadAsync(es, overwrite: true, cancellationToken: ct);
//                });
//                if (Guid.TryParse(correlationId, out var g2)) await _jobLogger.LogInfoAsync(g2, "Extracted entry {Entry} to {Dest}", entry.FullName, destName);
//            }
//            if (Guid.TryParse(correlationId, out var g3)) await _jobLogger.LogInfoAsync(g3, "Zip extracted for {Correlation}", correlationId);
//            _logger.LogInformation("Zip extracted for {Correlation}", correlationId);
//        }
//    }
//}
