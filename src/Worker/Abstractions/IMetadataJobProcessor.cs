using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface IMetadataJobProcessor
    {
        Task RunAsync(Guid jobId, string stagedZipBlobName, string country, string appName, CancellationToken ct);
    }
}
