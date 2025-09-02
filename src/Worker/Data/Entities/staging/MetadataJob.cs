using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data.Entities.staging
{
    public class MetadataJob
    {
        [Key] public Guid Id { get; set; }
        public Guid CorrelationId { get; set; } = default!;
        public string SourcePath { get; set; } = default!;
        public Guid ClientId { get; set; } = default!;
        public string Country { get; set; } = default!;
        public string AppName { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string Status { get; set; } = "Pending";
        public string? FailureReason { get; set; }
        public int TotalDocuments { get; set; }
        public int SuccessDocuments { get; set; }
        public int FailedDocuments { get; set; }
    }
}
