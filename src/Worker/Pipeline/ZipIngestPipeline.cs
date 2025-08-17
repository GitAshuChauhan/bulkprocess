using Microsoft.Extensions.Options;
using Worker.Configuration;
using Worker.Data.Repositories;
using Worker.Services;

namespace Worker.Pipeline
{
    public class ZipIngestPipeline
    {
        private readonly IMftClient _mft;
        private readonly IAzureClientFactory _af;
        private readonly IZipExtractor _extractor;
        private readonly IMetadataStager _stager;
        private readonly IDocumentProcessingService _processor;
        private readonly IDocumentRepository _repo;
        private readonly DocumentProcessingOptions _opts;
        private readonly ILogger<ZipIngestPipeline> _log;

        public ZipIngestPipeline(IMftClient mft, IAzureClientFactory af, IZipExtractor extractor,
            IMetadataStager stager, IDocumentProcessingService processor, IDocumentRepository repo,
            IOptions<DocumentProcessingOptions> opts, ILogger<ZipIngestPipeline> log)
        {
            _mft = mft; _af = af; _extractor = extractor; _stager = stager; _processor = processor; _repo = repo;
            _opts = opts.Value; _log = log;
        }

        public async Task RunAsync(string mftZipPath, Guid jobId, CancellationToken ct)
        {
            var bsc = _af.CreateBlobServiceClient();
            var stage = bsc.GetBlobContainerClient(_opts.StageContainer);
            await stage.CreateIfNotExistsAsync(cancellationToken: ct);

            var zipBlobName = $"{jobId:N}.zip";

            // 1) stream ZIP from MFT to stage
            await using (var mftStream = await _mft.DownloadAsync(mftZipPath, ct))
            {
                var zipBlob = stage.GetBlobClient(zipBlobName);
                await zipBlob.UploadAsync(mftStream, overwrite: true, cancellationToken: ct);
                _log.LogInformation("Uploaded zip to stage: {ZipBlob}", zipBlob.Uri);
            }

            // 2) extract zip entries to stage/{jobId}/...
            var metadataBlob = await _extractor.ExtractZipToStageAsync(_opts.StageContainer, zipBlobName, jobId.ToString("N"), _opts.MetadataFileName, ct);

            // 3) open metadata and stage rows in DB
            await using (var metaStream = await metadataBlob.OpenReadAsync(cancellationToken: ct))
            {
                await _stager.StageMetadataAsync(jobId, metaStream, ct);
            }

            // 4) process documents (server-side copy stage->prod + set tags)
            await _processor.ProcessJobAsync(jobId, ct);
        }
    }
}
