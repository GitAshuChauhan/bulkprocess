using Azure;
using Azure.Storage.Blobs;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Data.Entities;
using Worker.Data.Entities.staging;
using Worker.Data.Repositories;
using Worker.Infrastructure;

namespace Worker.Services
{
    public class ZipHandlerSftp : IZipHandler
    {
        private readonly BlobServiceClient _blobService;
        private readonly ResiliencePolicyFactory _policies;
        private readonly IConfiguration _cfg;
        private readonly ILogger<ZipHandlerSftp> _logger;
        private readonly IMftClient _mftClient;
        private readonly IStagingRepository _repository;
        private readonly IJobAlertService _alerts;
        private readonly IJobLogger _jobLogger;

        public ZipHandlerSftp(BlobServiceClient blobService, ResiliencePolicyFactory policies, IConfiguration cfg, ILogger<ZipHandlerSftp> logger, IMftClient mftClient, IStagingRepository repository, IJobAlertService alerts, IJobLogger jobLogger)
        {
            _blobService = blobService; 
            _policies = policies; 
            _cfg = cfg; 
            _logger = logger; 
            _mftClient = mftClient; 
            _repository = repository; 
            _alerts = alerts; 
            _jobLogger = jobLogger;
        }

        public async Task<ZipUploadResult> UploadZipFromMftAsync(Guid jobId, Guid correlationId, string mftZipPath, CancellationToken ct = default)
        {
            if (jobId == Guid.Empty) { var job = await _repository.GetOrCreateJobAsync(correlationId, mftZipPath, ct); jobId = job.Id; }

            var fileName = Path.GetFileName(mftZipPath) ?? $"{correlationId}.zip";
            var blobName = $"{correlationId}/{fileName}";
            var containerName = _cfg["Storage:StageContainer"] ?? "stage";
            var container = _blobService.GetBlobContainerClient(containerName);
            await _policies.BlobRetryPolicy.ExecuteAsync(async t => await container.CreateIfNotExistsAsync(cancellationToken: t), ct);

            var blobClient = container.GetBlobClient(blobName);
            var exists = (await blobClient.ExistsAsync(cancellationToken: ct)).Value;
            if (exists)
            {
                await _jobLogger.LogInfoAsync(jobId, $"Zip already staged {blobName}; skipping upload");
                return new ZipUploadResult(blobName, true);
            }

            try
            {
                var remoteExists = await _mftClient.ExistsAsync(mftZipPath, ct);
                if (!remoteExists) throw new FileNotFoundException($"Remote zip not found: {mftZipPath}");
            }
            catch (Exception ex)
            {
                _alerts.TrackDocumentFailure(jobId, mftZipPath, $"SFTP exists check failed: {ex.Message}");
                throw;
            }

            try
            {
                await _policies.BlobRetryPolicy.ExecuteAsync(async (ct2) =>
                {
                    await using var remoteStream = await _mftClient.OpenReadAsync(mftZipPath, ct2);
                    try
                    {
                        await blobClient.UploadAsync(remoteStream, overwrite: false, cancellationToken: ct2);
                    }
                    catch (RequestFailedException rf) when (rf.Status == 409 || rf.ErrorCode == "BlobAlreadyExists")
                    {
                        _logger.LogInformation("Concurrent blob creation detected {Blob}", blobName);
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                _alerts.TrackDocumentFailure(jobId, mftZipPath, $"Upload failed: {ex.Message}");
                throw;
            }

            await _jobLogger.LogInfoAsync(jobId, $"Uploaded zip to stage {blobName}");
            return new ZipUploadResult(blobName, false);
        }

        public async Task<Stream> StageZipAndExtractCsvAsync(MetadataJob job, Guid correlationId, CancellationToken ct = default)
        {
            var containerName = _cfg["Storage:StageContainer"] ?? "stage";
            var container = _blobService.GetBlobContainerClient(containerName);

            string? blobName = !string.IsNullOrWhiteSpace(job.SourcePath) ? job.SourcePath : null;
            if (blobName == null)
            {
                var prefix = $"{correlationId}/";
                await foreach (var b in container.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
                {
                    if (b.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { blobName = b.Name; break; }
                }
            }
            if (blobName == null) throw new FileNotFoundException($"No staged zip for correlationId={correlationId}");

            var blobClient = container.GetBlobClient(blobName);
            var exists = (await blobClient.ExistsAsync(cancellationToken: ct)).Value;
            if (!exists) throw new FileNotFoundException($"Staged blob missing: {blobName}");

            var blobStream = await blobClient.OpenReadAsync(cancellationToken: ct);
            var zip = new ZipArchive(blobStream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("metadata.csv", StringComparison.OrdinalIgnoreCase))
                     ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/metadata.csv", StringComparison.OrdinalIgnoreCase));
            if (entry == null) { zip.Dispose(); blobStream.Dispose(); throw new FileNotFoundException("metadata.csv not found"); }

            var entryStream = entry.Open();
            return new CompositeEntry(entryStream, zip, blobStream, _logger);
        }

        private sealed class CompositeEntry : Stream
        {
            private readonly Stream _inner; private readonly ZipArchive _zip; private readonly Stream _blob; private readonly ILogger _logger; private bool _disposed;
            public CompositeEntry(Stream inner, ZipArchive zip, Stream blob, ILogger logger) { _inner = inner; _zip = zip; _blob = blob; _logger = logger; _disposed = false; }
            public override bool CanRead => _inner.CanRead; public override bool CanSeek => _inner.CanSeek; public override bool CanWrite => false;
            public override long Length => _inner.Length; public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
            protected override void Dispose(bool disposing) { if (_disposed) return; if (disposing) { try { _inner.Dispose(); } catch { } try { _zip.Dispose(); } catch { } try { _blob.Dispose(); } catch { } } _disposed = true; base.Dispose(disposing); }
        }
    }
}
