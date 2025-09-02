using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data.Entities.production
{
    public class ProductionDocumentTag
    {
        [Key] public Guid Id { get; set; }
        public Guid ProductionDocumentId { get; set; }
        public string Key { get; set; } = default!;
        public string Value { get; set; } = default!;
    }
}
