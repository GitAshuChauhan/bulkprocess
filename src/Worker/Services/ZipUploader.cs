using Azure.Storage.Blobs;
using System.Threading;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Infrastructure;

namespace Worker.Services
{
    public class ZipUploader : IZipUploader
    {
        private readonly BlobServiceClient _bsc;
        private readonly IConfiguration _cfg;
        private readonly IMftClient _mft;
        private readonly ResiliencePolicyFactory _policies;

        public ZipUploader(BlobServiceClient bsc, IConfiguration cfg, IMftClient mft, ResiliencePolicyFactory policies)
        {
            _bsc = bsc ?? throw new ArgumentNullException(nameof(bsc));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _mft = mft ?? throw new ArgumentNullException(nameof(mft));
            _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        }

        public async Task<ZipUploadResult> UploadZipFromMftAsync(Guid jobId, string correlationId, string mftZipPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("correlationId is required", nameof(correlationId));
            if (string.IsNullOrWhiteSpace(mftZipPath)) throw new ArgumentException("mftZipPath is required", nameof(mftZipPath));

            var stage = _bsc.GetBlobContainerClient(_cfg["Storage:StageContainer"]);
            await _policies.BlobRetryPolicy.ExecuteAsync(async () => await stage.CreateIfNotExistsAsync(cancellationToken: ct));

            var dest = stage.GetBlobClient($"{correlationId}/upload.zip");

            var exists = await _policies.BlobRetryPolicy.ExecuteAsync(async () => await dest.ExistsAsync(ct));
            if (exists.Value)
            {
                return new ZipUploadResult(dest.Name, true);
            }

            await _policies.SftpRetryPolicy.ExecuteAsync(async () =>
            {
                await using var src = await _mft.OpenReadAsync(mftZipPath, ct);
                await _policies.BlobRetryPolicy.ExecuteAsync(async () =>
                {
                    await dest.UploadAsync(src, overwrite: true, cancellationToken: ct);
                });
            });

            return new ZipUploadResult(dest.Name, false);
        }
    }
}
