using Azure.Storage.Blobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Services
{
    public interface IZipExtractor
    {
        /// <summary>
        /// Extract zip blob at zipBlobPath (in stage container) into stageContainer under prefix (e.g. jobId).
        /// Returns the blob client of the metadata.json that was written.
        /// </summary>
        Task<BlobClient> ExtractZipToStageAsync(string stageContainer, string zipBlobPath, string targetPrefix, string metadataFileName, CancellationToken ct);
    }
}
