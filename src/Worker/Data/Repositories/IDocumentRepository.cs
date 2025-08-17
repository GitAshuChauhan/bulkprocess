using Worker.Data;

namespace Worker.Data.Repositories
{
    public interface IDocumentRepository
    {
        Task<MetadataJob> CreateJobAsync(string sourcePath, CancellationToken ct);
        Task BulkInsertDocumentsAsync(IEnumerable<DocumentEntity> docs, int batchSize, CancellationToken ct);
        Task<List<DocumentEntity>> GetPendingDocumentsAsync(Guid jobId, int take, CancellationToken ct);
        Task MarkProcessingAsync(Guid id, CancellationToken ct);
        Task MarkSuccessAsync(Guid id, CancellationToken ct);
        Task MarkFailedAsync(Guid id, string error, CancellationToken ct);
        Task SetJobStatusAsync(Guid jobId, string status, CancellationToken ct);
    }
}
