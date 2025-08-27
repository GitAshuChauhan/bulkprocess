using Azure.Storage.Blobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Data.Entities;

namespace Worker.Abstractions
{
    public interface IMetadataStager
    {
        Task StageMetadataAsync(Guid jobId, BlobClient metadataBlob, CancellationToken ct = default);
    }
}
