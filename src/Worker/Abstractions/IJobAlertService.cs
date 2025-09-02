using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface IJobAlertService
    {
        void TrackJobStarted(Guid jobId, string? clientId = null);
        void TrackJobCompleted(Guid jobId, bool success, string? failureReason = null);
        void TrackDocumentFailure(Guid jobId, string filePath, string errorMessage);
        void TrackRetry(string operation, int attempt, Guid? jobId = null);
        void TrackCustomMetric(string name, double value, Guid? jobId = null);
        void Flush();
    }
}
