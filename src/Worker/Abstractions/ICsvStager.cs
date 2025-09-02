using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface ICsvStager
    {
        Task StageCsvAsync(Stream csvStream, Guid jobId, CancellationToken ct = default);
    }
}
