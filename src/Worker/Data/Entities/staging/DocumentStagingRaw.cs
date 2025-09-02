using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data.Entities.staging
{
    public enum StagingRowStatus { Pending = 0, Processing = 1, Succeeded = 2, Failed = 3 }

    public class DocumentStagingRaw
    {
        public Guid Id { get; set; }
        public Guid JobId { get; set; }
        public string RawData { get; set; } = default!;
        public StagingRowStatus Status { get; set; } = StagingRowStatus.Pending;
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastUpdated { get; set; }
    }
}
