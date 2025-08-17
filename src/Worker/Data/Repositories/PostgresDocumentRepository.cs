using Microsoft.EntityFrameworkCore;
using Worker.Resilience;
using Worker.Data;

namespace Worker.Data.Repositories
{
    public class PostgresDocumentRepository : IDocumentRepository
    {
        private readonly DataContext _db;
        private readonly ResiliencePolicyFactory _policies;

        public PostgresDocumentRepository(DataContext db, ResiliencePolicyFactory policies)
        {
            _db = db; _policies = policies;
        }

        public async Task<MetadataJob> CreateJobAsync(string sourcePath, CancellationToken ct)
        {
            return await _policies.DbRetryPolicy.ExecuteAsync(async token =>
            {
                var job = new MetadataJob { Id = Guid.NewGuid(), SourcePath = sourcePath, Status = "Processing", CreatedAt = DateTimeOffset.UtcNow };
                _db.MetadataJobs.Add(job);
                await _db.SaveChangesAsync(token);
                return job;
            }, ct);
        }

        public async Task BulkInsertDocumentsAsync(IEnumerable<DocumentEntity> docs, int batchSize, CancellationToken ct)
        {
            var chunks = docs.Select((v, i) => new { v, i })
                             .GroupBy(x => x.i / Math.Max(1, batchSize))
                             .Select(g => g.Select(x => x.v).ToList());

            foreach (var chunk in chunks)
            {
                await _policies.DbRetryPolicy.ExecuteAsync(async token =>
                {
                    _db.Documents.AddRange(chunk);
                    await _db.SaveChangesAsync(token);
                }, ct);
            }
        }

        public Task<List<DocumentEntity>> GetPendingDocumentsAsync(Guid jobId, int take, CancellationToken ct)
        {
            return _db.Documents.AsNoTracking()
                .Where(d => d.JobId == jobId && d.Status == DocumentStatus.Pending)
                .OrderBy(d => d.Id)
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task MarkProcessingAsync(Guid id, CancellationToken ct)
        {
            await _db.Documents.Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DocumentStatus.Processing)
                                         .SetProperty(d => d.LastUpdated, DateTimeOffset.UtcNow), ct);
        }

        public async Task MarkSuccessAsync(Guid id, CancellationToken ct)
        {
            await _db.Documents.Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DocumentStatus.Success)
                                         .SetProperty(d => d.Error, default(string))
                                         .SetProperty(d => d.LastUpdated, DateTimeOffset.UtcNow), ct);
        }

        public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct)
        {
            await _db.Documents.Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DocumentStatus.Failed)
                                         .SetProperty(d => d.Error, error)
                                         .SetProperty(d => d.LastUpdated, DateTimeOffset.UtcNow), ct);
        }

        public async Task SetJobStatusAsync(Guid jobId, string status, CancellationToken ct)
        {
            await _db.MetadataJobs.Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, status), ct);
        }
    }
}
