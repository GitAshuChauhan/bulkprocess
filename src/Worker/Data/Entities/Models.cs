using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data.Entities
{
    public enum DocumentStatus { Pending = 0, Processing = 1, Success = 2, Failed = 3 }


    public class MetadataJob
    {
        [Key] public Guid Id { get; set; }
        [Required] public string ClientId { get; set; } = default!; //[TODO]change to guidid
        [Required] public string CorrelationId { get; set; } = default!;//[TODO]change to guidid
        [Required] public string SourcePath { get; set; } = default!;
        [Required] public string Country { get; set; } = default!;
        [Required] public string AppName { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        [Required] public string Status { get; set; } = "Pending";
        public string? FailureReason { get; set; }
        public int TotalDocuments { get; set; }
        public int SuccessDocuments { get; set; }
        public int FailedDocuments { get; set; }
    }

    public class DocumentEntity
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid JobId { get; set; }
        [Required] public string FileGuid { get; set; } = default!;
        [Required] public string Filepath { get; set; } = default!;
        [Required] public string Extension { get; set; } = default!;
        public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
        public string? Error { get; set; }
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }

    public class ProductionDocumentEntity
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid JobId { get; set; }
        [Required] public string FileGuid { get; set; } = default!;
        [Required] public string BlobName { get; set; } = default!;
        [Required] public string Extension { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<ProductionDocumentTag> Tags { get; set; } = new();
    }

    public class ProductionDocumentTag
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid ProductionDocumentId { get; set; }
        [Required] public string TagKey { get; set; } = default!;
        [Required] public string TagValue { get; set; } = default!;
    }

    public class InboundMessage
    {
        public string FolderName { get; set; } = default!;
        public string MftPath { get; set; } = default!;
        public string Country { get; set; } = default!;
        public string AppName { get; set; } = default!;
    }

    public class MetadataDto
    {
        public string country { get; set; } = default!;
        public string appname { get; set; } = default!;
        public List<DocTypeDto> doctypes { get; set; } = new();
    }
    public class DocTypeDto
    {
        public string doctype { get; set; } = default!;
        public List<DocumentDto> documents { get; set; } = new();
    }
    public class TagDto : Dictionary<string, string> { }
    public class DocumentDto
    {
        public string filepath { get; set; } = default!;
        public string fileguid { get; set; } = default!;
        public string extension { get; set; } = default!;
        public List<TagDto>? tags { get; set; }
    }

}
