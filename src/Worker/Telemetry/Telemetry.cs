using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Worker.Telemetry
{
    public static class Telemetry
    {
        public const string SourceName = "bulkupload-processor";
        public static readonly ActivitySource Source = new(SourceName);
    }
}
