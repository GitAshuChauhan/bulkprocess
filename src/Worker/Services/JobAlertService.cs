using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Worker.Abstractions;

namespace Worker.Services
{
    public class JobAlertService : IJobAlertService
    {
        //[TODO]use Activity for telemetry correlation.This is agnostic to observability platform. TelemetryClient locked into App Insights.
        private readonly TelemetryClient _telemetry;
        private readonly ILogger<JobAlertService> _logger;

        public JobAlertService(TelemetryConfiguration config, ILogger<JobAlertService> logger)
        {
            _telemetry = new TelemetryClient(config);
            _logger = logger;
        }

        public void TrackJobStarted(Guid jobId, string? clientId = null)
        {
            var props = new Dictionary<string, string> { { "JobId", jobId.ToString() } };
            if (!string.IsNullOrEmpty(clientId)) props["ClientId"] = clientId;
            _telemetry.TrackEvent("JobStarted", props);
            _logger.LogInformation("Telemetry JobStarted {JobId}", jobId);
        }

        public void TrackJobCompleted(Guid jobId, bool success, string? failureReason = null)
        {
            var props = new Dictionary<string, string> { { "JobId", jobId.ToString() }, { "Success", success.ToString() } };
            if (!string.IsNullOrEmpty(failureReason)) props["FailureReason"] = failureReason;
            _telemetry.TrackEvent("JobCompleted", props);
            _logger.LogInformation("Telemetry JobCompleted {JobId} Success={Success}", jobId, success);
        }

        public void TrackDocumentFailure(Guid jobId, string filePath, string errorMessage)
        {
            var ex = new Exception(errorMessage);
            var props = new Dictionary<string, string> { { "JobId", jobId.ToString() }, { "FilePath", filePath } };
            _telemetry.TrackException(ex, props);
            _logger.LogError("Telemetry DocumentFailure job={JobId} file={FilePath} err={Err}", jobId, filePath, errorMessage);
        }

        public void TrackRetry(string operation, int attempt, Guid? jobId = null)
        {
            var props = new Dictionary<string, string> { { "Operation", operation }, { "Attempt", attempt.ToString() } };
            if (jobId.HasValue) props["JobId"] = jobId.Value.ToString();
            _telemetry.TrackEvent("RetryAttempt", props);
            _logger.LogWarning("Telemetry RetryAttempt op={Operation} attempt={Attempt} job={JobId}", operation, attempt, jobId);
        }

        public void TrackCustomMetric(string name, double value, Guid? jobId = null)
        {
            _telemetry.GetMetric(name).TrackValue(value);
            var props = new Dictionary<string, string> { { "Metric", name }, { "Value", value.ToString() } };
            if (jobId.HasValue) props["JobId"] = jobId.Value.ToString();
            _telemetry.TrackEvent("CustomMetric", props);
            _logger.LogInformation("Telemetry CustomMetric {Metric}={Value}", name, value);
        }

        public void Flush()
        {
            try { _telemetry.Flush(); } catch (Exception ex) { _logger.LogWarning(ex, "Telemetry flush failed"); }
        }
    }
}
