using System.IO.Compression;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Worker.Services
{
    public class ZipExtractor : IZipExtractor
    {
        private readonly BlobServiceClient _bsc;
        private readonly ILogger<ZipExtractor> _log;

        public ZipExtractor(IAzureClientFactory fac, ILogger<ZipExtractor> log)
        {
            _bsc = fac.CreateBlobServiceClient();
            _log = log;
        }

        public async Task<BlobClient> ExtractZipToStageAsync(string stageContainer, string zipBlobPath, string targetPrefix, string metadataFileName, CancellationToken ct)
        {
            var container = _bsc.GetBlobContainerClient(stageContainer);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var zipBlob = container.GetBlobClient(zipBlobPath);

            BlobClient? metadataBlob = null;
            // stream the zip blob and extract entry-by-entry to stage/{targetPrefix}/<entry>
            await using var zipStream = await zipBlob.OpenReadAsync(cancellationToken: ct);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

            foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
            {
                ct.ThrowIfCancellationRequested();
                var normalized = entry.FullName.Replace("\\", "/");
                var destPath = string.IsNullOrEmpty(targetPrefix) ? normalized : $"{targetPrefix.TrimEnd('/')}/{normalized}";
                var destBlob = container.GetBlobClient(destPath);

                _log.LogDebug("Extracting entry {Entry} -> {Dest}", entry.FullName, destPath);
                await using var es = entry.Open();
                // stream entry directly to blob
                await destBlob.UploadAsync(es, overwrite: true, cancellationToken: ct);

                if (string.Equals(entry.Name, metadataFileName, StringComparison.OrdinalIgnoreCase))
                {
                    metadataBlob = destBlob;
                }
            }

            if (metadataBlob == null) throw new FileNotFoundException($"Metadata file {metadataFileName} not found in ZIP {zipBlobPath}");
            _log.LogInformation("Extraction finished. Metadata blob: {Uri}", metadataBlob.Uri);
            return metadataBlob;
        }
    }
}
