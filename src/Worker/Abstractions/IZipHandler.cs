using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Data.Entities;
using Worker.Data.Entities.staging;

namespace Worker.Abstractions
{
    public record ZipUploadResult(string BlobName, bool Skipped);

    public interface IZipHandler
    {
        Task<ZipUploadResult> UploadZipFromMftAsync(Guid jobId, Guid correlationId, string mftZipPath, CancellationToken ct = default);
        Task<Stream> StageZipAndExtractCsvAsync(MetadataJob job, Guid correlationId, CancellationToken ct = default);
    }
}
