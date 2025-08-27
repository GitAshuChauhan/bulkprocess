using Azure.Storage.Blobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface IZipExtractor
    {
        Task ExtractToContainerAsync(BlobClient zipBlob, BlobContainerClient stageContainer, string correlationId, CancellationToken ct);
    }
}
