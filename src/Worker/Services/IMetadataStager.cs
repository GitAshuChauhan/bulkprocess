using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Services
{
    public interface IMetadataStager
    {
        Task StageMetadataAsync(Guid jobId, Stream metadataJsonStream, CancellationToken ct);
    }
}
