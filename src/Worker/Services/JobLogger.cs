using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Data;
using Worker.Data.DbContext;
using Worker.Data.Entities;
using Worker.Data.Entities.staging;

namespace Worker.Services
{
    public class JobLogger : IJobLogger
    {
        private readonly StagingDbContext _context;
        private readonly ILogger<JobLogger> _logger;

        public JobLogger(StagingDbContext context, ILogger<JobLogger> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogJobStartAsync(Guid jobId, Guid correlationId, Guid clientId, CancellationToken cancellationToken = default)
        {
            var job = await _context.MetadataJobs.FindAsync(new object[] { jobId }, cancellationToken);
            if (job != null)
            {
                job.StartedAt = DateTimeOffset.UtcNow;
                job.Status = "Processing";
                job.CorrelationId = correlationId;
                job.ClientId = clientId;
                await _context.SaveChangesAsync(cancellationToken);
            }
            _logger.LogInformation("Job {JobId} started for Client {ClientId}, Correlation {CorrelationId}", jobId, clientId, correlationId);
        }

        public async Task LogJobCompletionAsync(Guid jobId, bool success, string? errorMessage = null, CancellationToken cancellationToken = default)
        {
            var job = await _context.MetadataJobs.FindAsync(new object[] { jobId }, cancellationToken);
            if (job != null)
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Status = success ? "Completed" : "Failed";
                job.FailureReason = errorMessage;
                await _context.SaveChangesAsync(cancellationToken);
            }
            _logger.LogInformation("Job {JobId} completed with status {Status}", jobId, success ? "Completed" : "Failed");
        }

        public async Task LogDocumentStartAsync(Guid jobId, Guid documentId, string filePath, CancellationToken cancellationToken = default)
        {
            var doc = await _context.DocumentStagingRaws.FindAsync(new object[] { documentId }, cancellationToken);
            if (doc != null)
            {
                doc.Status = StagingRowStatus.Processing;
                doc.LastUpdated = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
            _logger.LogInformation("Document {DocumentId} ({FilePath}) for Job {JobId} started processing", documentId, filePath, jobId);
        }

        public async Task LogDocumentCompletionAsync(Guid jobId, Guid documentId, bool success, string? error = null, CancellationToken ct = default)
        {
            var doc = await _context.DocumentStagingRaws.FindAsync(new object[] { documentId }, ct);
            if (doc != null)
            {
                doc.Status = success ? StagingRowStatus.Succeeded : StagingRowStatus.Failed;
                doc.Error = error;
                doc.LastUpdated = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
            if (success) await IncrementJobCountersAsync(jobId, true);
            else await IncrementJobCountersAsync(jobId, false);
            _logger.LogInformation("Doc {DocId} complete for job {JobId} success={Success} error={Error}", documentId, jobId, success, error);
        }

        //[TODO: Increment of job counter can be done in bulk instead of for each document record.]
        private async Task IncrementJobCountersAsync(Guid jobId, bool success)
        {
            var job = await _context.MetadataJobs.FindAsync(new object[] { jobId });
            if (job == null) return;
            if (success) job.SuccessDocuments += 1; else job.FailedDocuments += 1;
            await _context.SaveChangesAsync();
        }

        public Task LogInfoAsync(Guid jobId, string message, CancellationToken ct = default)
        {
            _logger.LogInformation("[Job {JobId}] {Message}", jobId, message);
            return Task.CompletedTask;
        }

        public Task LogErrorAsync(Guid jobId, string message, Exception? ex = null, CancellationToken ct = default)
        {
            _logger.LogError(ex, "[Job {JobId}] {Message}", jobId, message);
            return Task.CompletedTask;
        }
    }
}
