using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Data.Entities;
using Worker.Infrastructure;
using Worker.Telemetry;

namespace Worker.Data.Repositories
{
    public class PostgresDocumentRepository : IDocumentRepository
    {
        private readonly DataContext _ctx;
        private readonly ResiliencePolicyFactory _policies;

        public PostgresDocumentRepository(DataContext ctx, ResiliencePolicyFactory policies)
        {
            _ctx = ctx;
            _policies = policies;
        }

        public async Task<MetadataJob> GetOrCreateMetadataJobAsync(string correlationId, string sourcePath, string country, string appName)
        {
            var existing = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.MetadataJobs.FirstOrDefaultAsync(j => j.CorrelationId == correlationId));
            if (existing != null)
            {
                var changed = false;
                if (string.IsNullOrWhiteSpace(existing.SourcePath) && !string.IsNullOrWhiteSpace(sourcePath)) { existing.SourcePath = sourcePath; changed = true; }
                if (string.IsNullOrWhiteSpace(existing.Country) && !string.IsNullOrWhiteSpace(country)) { existing.Country = country; changed = true; }
                if (string.IsNullOrWhiteSpace(existing.AppName) && !string.IsNullOrWhiteSpace(appName)) { existing.AppName = appName; changed = true; }
                if (changed) await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync());
                return existing;
            }

            var job = new MetadataJob { Id = Guid.NewGuid(), CorrelationId = correlationId, SourcePath = sourcePath, Country = country, AppName = appName, CreatedAt = DateTimeOffset.UtcNow, Status = "Pending" };
            await _policies.DbRetryPolicy.ExecuteAsync(async () => { _ctx.MetadataJobs.Add(job); await _ctx.SaveChangesAsync(); });
            return job;
        }

        public async Task EnsureJobStartedAsync(Guid jobId)
        {
            var job = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.MetadataJobs.FindAsync(jobId));
            if (job == null) return;
            if (job.StartedAt == null) { job.StartedAt = DateTimeOffset.UtcNow; job.Status = "Processing"; await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync()); }
        }

        public async Task MarkJobCompletedAsync(Guid jobId)
        {
            var job = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.MetadataJobs.FindAsync(jobId));
            if (job == null) return;
            job.CompletedAt = DateTimeOffset.UtcNow; job.Status = "Completed"; await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync());
        }

        public async Task MarkJobFailedAsync(Guid jobId, string reason)
        {
            var job = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.MetadataJobs.FindAsync(jobId));
            if (job == null) return;
            job.CompletedAt = DateTimeOffset.UtcNow; job.Status = "Failed"; job.FailureReason = reason; await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync());
        }

        public async Task AddDocumentsAsync(IEnumerable<DocumentEntity> docs)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                foreach (var d in docs)
                {
                    var exists = await _ctx.Documents.AnyAsync(x => x.JobId == d.JobId && x.FileGuid == d.FileGuid);
                    if (!exists) _ctx.Documents.Add(d);
                }
                await _ctx.SaveChangesAsync();
            });
        }

        public async Task<(IReadOnlyList<DocumentEntity> Docs, int TotalPending)> GetPendingByJobBatchAsync(Guid jobId, int skip, int take, CancellationToken ct)
        {
            var query = _ctx.Documents.AsNoTracking().Where(d => d.JobId == jobId && (d.Status == DocumentStatus.Pending || d.Status == DocumentStatus.Failed)).OrderBy(d => d.Id);
            var total = await _policies.DbRetryPolicy.ExecuteAsync(async () => await query.CountAsync(ct));
            var page = await _policies.DbRetryPolicy.ExecuteAsync(async () => await query.Skip(skip).Take(take).ToListAsync(ct));
            return (page, total);
        }

        public async Task UpdateDocumentStatusAsync(Guid id, DocumentStatus status, string? error = null)
        {
            var doc = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.Documents.FindAsync(id));
            if (doc == null) return;
            doc.Status = status; doc.Error = error; doc.LastUpdated = DateTimeOffset.UtcNow; await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync());
        }

        public async Task IncrementJobCountersAsync(Guid jobId, bool success)
        {
            var job = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.MetadataJobs.FindAsync(jobId));
            if (job == null) return;
            if (success) job.SuccessDocuments += 1; else job.FailedDocuments += 1;
            await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync());
        }

        public async Task AddProductionRecordWithTagsAsync(Guid jobId, string fileGuid, string blobName, string extension, IDictionary<string, string> tags, CancellationToken ct)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                var prodDoc = new ProductionDocumentEntity { Id = Guid.NewGuid(), JobId = jobId, FileGuid = fileGuid, BlobName = blobName, Extension = extension, CreatedAt = DateTimeOffset.UtcNow };
                _ctx.ProductionDocuments.Add(prodDoc);
                await _ctx.SaveChangesAsync(ct);
                if (tags.Count > 0)
                {
                    var tagRows = tags.Select(kv => new ProductionDocumentTag { Id = Guid.NewGuid(), ProductionDocumentId = prodDoc.Id, TagKey = kv.Key, TagValue = kv.Value });
                    _ctx.ProductionDocumentTags.AddRange(tagRows);
                    await _ctx.SaveChangesAsync(ct);
                }
            });
        }
    }
}
