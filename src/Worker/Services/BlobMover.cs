using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Worker.Resilience;

namespace Worker.Services
{
    public class BlobMover : IBlobMover
    {
        private readonly BlobServiceClient _bsc;
        private readonly ILogger<BlobMover> _logger;
        private readonly ResiliencePolicyFactory _policies;

        public BlobMover(IAzureClientFactory fac, ILogger<BlobMover> logger, ResiliencePolicyFactory policies)
        {
            _bsc = fac.CreateBlobServiceClient();
            _logger = logger;
            _policies = policies;
        }

        public async Task MoveWithTagsAsync(string stageContainer, string prodContainer, string blobName, IDictionary<string, string> tags, CancellationToken ct)
        {
            var stage = _bsc.GetBlobContainerClient(stageContainer).GetBlobClient(blobName);
            var prod = _bsc.GetBlobContainerClient(prodContainer).GetBlobClient(blobName);

            // kick off copy
            await _policies.StorageRetry.ExecuteAsync(async token =>
            {
                await prod.StartCopyFromUriAsync(stage.Uri, cancellationToken: token);
            }, ct);

            // poll for completion
            await _policies.StorageRetry.ExecuteAsync(async token =>
            {
                while (true)
                {
                    var props = await prod.GetPropertiesAsync(cancellationToken: token);
                    if (props.Value.CopyStatus != CopyStatus.Pending) break;
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
            }, ct);

            // apply tags
            if (tags != null && tags.Count > 0)
            {
                await _policies.StorageRetry.ExecuteAsync(token => prod.SetTagsAsync(tags, cancellationToken:token), ct);
            }
            // delete source
            await _policies.StorageRetry.ExecuteAsync(token => stage.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: token), ct);

            _logger.LogInformation("Moved {Blob} stage→prod and tagged", blobName);
        }
    }
}
