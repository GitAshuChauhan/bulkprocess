using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data
{
    public class MetadataJob
    {
        public Guid Id { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public ICollection<DocumentEntity> Documents { get; set; } = new List<DocumentEntity>();
    }

    public enum DocumentStatus { Pending, Processing, Success, Failed }

    public class DocumentEntity
    {
        public Guid Id { get; set; }
        public Guid JobId { get; set; }
        public MetadataJob? Job { get; set; }
        public string FileGuid { get; set; } = string.Empty;
        public string Filepath { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string DocType { get; set; } = string.Empty;
        public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
        public string? Error { get; set; }
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }
}
