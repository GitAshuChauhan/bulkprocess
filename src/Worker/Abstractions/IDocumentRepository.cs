using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Data.Entities;

namespace Worker.Abstractions
{
    public interface IDocumentRepository
    {
        Task<MetadataJob> GetOrCreateMetadataJobAsync(string correlationId, string sourcePath, string country, string appName);
        Task EnsureJobStartedAsync(Guid jobId);
        Task MarkJobCompletedAsync(Guid jobId);
        Task MarkJobFailedAsync(Guid jobId, string reason);
        Task AddDocumentsAsync(IEnumerable<DocumentEntity> docs);
        Task<(IReadOnlyList<DocumentEntity> Docs, int TotalPending)> GetPendingByJobBatchAsync(Guid jobId, int skip, int take, CancellationToken ct);
        Task UpdateDocumentStatusAsync(Guid id, DocumentStatus status, string? error = null);
        Task IncrementJobCountersAsync(Guid jobId, bool success);
        Task AddProductionRecordWithTagsAsync(Guid jobId, string fileGuid, string blobName, string extension, IDictionary<string, string> tags, CancellationToken ct);
    }
}
