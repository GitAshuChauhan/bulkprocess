using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public record ZipUploadResult(string BlobName, bool Skipped);
    public interface IZipUploader
    {
        Task<ZipUploadResult> UploadZipFromMftAsync(Guid jobId, string correlationId, string mftZipPath, CancellationToken ct = default);
    }
}
