using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Services
{
    public interface IDocumentProcessingService
    {
        Task ProcessJobAsync(Guid jobId, CancellationToken ct);
    }
}
