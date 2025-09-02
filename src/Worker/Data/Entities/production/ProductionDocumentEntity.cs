using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data.Entities.production
{
    public class ProductionDocumentEntity
    {
        [Key] public Guid Id { get; set; }
        public Guid JobId { get; set; }
        public string FileGuid { get; set; } = default!;
        public string FileName { get; set; } = default!;
        public string Extension { get; set; } = default!;
        public string BlobUrl { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public ICollection<ProductionDocumentTag> Tags { get; set; } = new List<ProductionDocumentTag>();
    }
}
