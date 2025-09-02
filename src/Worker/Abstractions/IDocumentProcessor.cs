using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface IDocumentProcessor
    {
        Task ProcessJobAsync(Guid jobId, CancellationToken ct = default);
    }
}
