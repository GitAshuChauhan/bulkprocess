using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Abstractions;
using Microsoft.EntityFrameworkCore;
using Worker.Data.DbContext;
using Worker.Data.Entities.production;
using Worker.Data.Entities.staging;
using Worker.Infrastructure;
using Worker.Data.Helper;

namespace Worker.Data.Repositories
{
    public class StagingRepository : IStagingRepository
    {
        private readonly StagingDbContext _ctx;
        private readonly ResiliencePolicyFactory _policies;
        private readonly ILogger<StagingRepository> _logger;

        public StagingRepository(StagingDbContext ctx, ResiliencePolicyFactory policies, ILogger<StagingRepository> logger)
        {
            _ctx = ctx; _policies = policies; _logger = logger;
        }

        public async Task<MetadataJob> GetOrCreateJobAsync(Guid correlationId, string? sourcePath = null, CancellationToken ct = default)
        {
            return await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                var existing = await _ctx.MetadataJobs.FirstOrDefaultAsync(j => j.CorrelationId == correlationId, ct);
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(sourcePath) && string.IsNullOrWhiteSpace(existing.SourcePath))
                    {
                        existing.SourcePath = sourcePath;
                        await _ctx.SaveChangesAsync(ct);
                    }
                    return existing;
                }

                var job = new MetadataJob
                {
                    Id = Guid.NewGuid(),
                    CorrelationId = correlationId,
                    SourcePath = sourcePath ?? string.Empty,
                    Status = "Pending",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _ctx.MetadataJobs.Add(job);
                await _ctx.SaveChangesAsync(ct);
                _logger.LogInformation("Created job {JobId}", job.Id);
                return job;
            });
        }

        public async Task SetJobStartedAsync(Guid jobId, CancellationToken ct = default)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                var job = await _ctx.MetadataJobs.FindAsync(new object[] { jobId }, ct);
                if (job != null)
                {
                    job.Status = "Processing";
                    job.StartedAt ??= DateTimeOffset.UtcNow;
                    await _ctx.SaveChangesAsync(ct);
                }
            });
        }

        public async Task SetJobCompletedAsync(Guid jobId, int success, int failed, string? reason = null, CancellationToken ct = default)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                var job = await _ctx.MetadataJobs.FindAsync(new object[] { jobId }, ct);
                if (job != null)
                {
                    job.Status = failed == 0 ? "Completed" : "CompletedWithErrors";
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    job.SuccessDocuments = success;
                    job.FailedDocuments = failed;
                    job.FailureReason = reason;
                    await _ctx.SaveChangesAsync(ct);
                }
            });
        }

        public async Task SetJobFailedAsync(Guid jobId, string reason, CancellationToken ct = default)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                var job = await _ctx.MetadataJobs.FindAsync(new object[] { jobId }, ct);
                if (job != null)
                {
                    job.Status = "Failed";
                    job.FailureReason = reason;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    await _ctx.SaveChangesAsync(ct);
                }
            });
        }

        public async Task<IReadOnlyList<DocumentStagingRaw>> GetPendingStagingRowsAsync(Guid jobId, int take, CancellationToken ct = default)
        {
            return await _policies.DbRetryPolicy.ExecuteAsync(async () =>
                await _ctx.DocumentStagingRaws.Where(r => r.JobId == jobId && r.Status == StagingRowStatus.Pending)
                    .OrderBy(r => r.CreatedAt)
                    .Take(take)
                    .ToListAsync(ct));
        }

        public async Task<int> CountPendingByJobAsync(Guid jobId, CancellationToken ct = default)
        {
            return await _policies.DbRetryPolicy.ExecuteAsync(async () =>
                await _ctx.DocumentStagingRaws.CountAsync(r => r.JobId == jobId && r.Status == StagingRowStatus.Pending, ct));
        }

        public async Task UpdateStagingRowStatusAsync(Guid stagingId, string status, string? error, CancellationToken ct = default)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                var row = await _ctx.DocumentStagingRaws.FindAsync(new object[] { stagingId }, ct);
                if (row != null)
                {
                    row.Status = Enum.Parse<StagingRowStatus>(status);
                    row.Error = error;
                    await _ctx.SaveChangesAsync(ct);
                }
            });
        }

        //public async Task SaveProductionDocumentAsync(ProductionDocumentEntity prod, IEnumerable<ProductionDocumentTag> tags, CancellationToken ct = default)
        //{
        //    await _policies.DbRetryPolicy.ExecuteAsync(async () =>
        //    {
        //        var exists = await _ctx.ProductionDocuments.AnyAsync(p => p.JobId == prod.JobId && p.FileGuid == prod.FileGuid, ct);
        //        if (exists) return;
        //        _ctx.ProductionDocuments.Add(prod);
        //        if (tags != null && tags.Any()) _ctx.ProductionDocumentTags.AddRange(tags);
        //        await _ctx.SaveChangesAsync(ct);
        //    });
        //}

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

        //public async Task AddDocumentsAsync(IEnumerable<DocumentEntity> docs)
        //{
        //    await _policies.DbRetryPolicy.ExecuteAsync(async () =>
        //    {
        //        foreach (var d in docs)
        //        {
        //            var exists = await _ctx.Documents.AnyAsync(x => x.JobId == d.JobId && x.FileGuid == d.FileGuid);
        //            if (!exists) _ctx.Documents.Add(d);
        //        }
        //        await _ctx.SaveChangesAsync();
        //    });
        //}

        //public async Task<(IReadOnlyList<DocumentEntity> Docs, int TotalPending)> GetPendingByJobBatchAsync(Guid jobId, int skip, int take, CancellationToken ct)
        //{
        //    var query = _ctx.Documents.AsNoTracking().Where(d => d.JobId == jobId && (d.Status == DocumentStatus.Pending || d.Status == DocumentStatus.Failed)).OrderBy(d => d.Id);
        //    var total = await _policies.DbRetryPolicy.ExecuteAsync(async () => await query.CountAsync(ct));
        //    var page = await _policies.DbRetryPolicy.ExecuteAsync(async () => await query.Skip(skip).Take(take).ToListAsync(ct));
        //    return (page, total);
        //}

        //public async Task UpdateDocumentStatusAsync(Guid id, DocumentStatus status, string? error = null)
        //{
        //    var doc = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.Documents.FindAsync(id));
        //    if (doc == null) return;
        //    doc.Status = status; doc.Error = error; doc.LastUpdated = DateTimeOffset.UtcNow; await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync());
        //}

        public async Task IncrementJobCountersAsync(Guid jobId, bool success)
        {
            var job = await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.MetadataJobs.FindAsync(jobId));
            if (job == null) return;
            if (success) job.SuccessDocuments += 1; else job.FailedDocuments += 1;
            await _policies.DbRetryPolicy.ExecuteAsync(async () => await _ctx.SaveChangesAsync());
        }

        //public async Task AddProductionRecordWithTagsAsync(Guid jobId, string fileGuid, string blobName, string extension, IDictionary<string, string> tags, CancellationToken ct)
        //{
        //    await _policies.DbRetryPolicy.ExecuteAsync(async () =>
        //    {
        //        var prodDoc = new ProductionDocumentEntity { Id = Guid.NewGuid(), JobId = jobId, FileGuid = fileGuid, BlobName = blobName, Extension = extension, CreatedAt = DateTimeOffset.UtcNow };
        //        _ctx.ProductionDocuments.Add(prodDoc);
        //        await _ctx.SaveChangesAsync(ct);
        //        if (tags.Count > 0)
        //        {
        //            var tagRows = tags.Select(kv => new ProductionDocumentTag { Id = Guid.NewGuid(), ProductionDocumentId = prodDoc.Id, TagKey = kv.Key, TagValue = kv.Value });
        //            _ctx.ProductionDocumentTags.AddRange(tagRows);
        //            await _ctx.SaveChangesAsync(ct);
        //        }
        //    });
        //}
    }
}
