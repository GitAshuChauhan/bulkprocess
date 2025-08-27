using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Telemetry
{
    public static class Metrics
    {
        public const string MeterName = "MFTProcessor.Worker";
        private static readonly Meter m = new(MeterName, "1.0.0");
        public static readonly Counter<int> JobsProcessed = m.CreateCounter<int>("jobs_processed_total");
        public static readonly Counter<int> JobFailures = m.CreateCounter<int>("jobs_failed_total");
        public static readonly Counter<int> DocumentsProcessed = m.CreateCounter<int>("documents_processed_total");
        public static readonly Counter<int> DocumentFailures = m.CreateCounter<int>("documents_failed_total");
    }
}
