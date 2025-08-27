using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface IJobLogger
    {
        Task LogJobStartAsync(Guid jobId, string correlationId, string clientId, CancellationToken ct = default);
        Task LogJobCompletionAsync(Guid jobId, bool success, string? error = null, CancellationToken ct = default);
        Task LogDocumentStartAsync(Guid jobId, Guid documentId, string filePath, CancellationToken ct = default);
        Task LogDocumentCompletionAsync(Guid jobId, Guid documentId, bool success, string? error = null, CancellationToken ct = default);
        Task LogInfoAsync(Guid jobId, string message, CancellationToken ct = default);
    }
}
