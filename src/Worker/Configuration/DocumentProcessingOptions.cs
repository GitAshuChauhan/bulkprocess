using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Configuration
{
    public sealed class DocumentProcessingOptions
    {
        public int MaxDegreeOfParallelism { get; set; } = 100;
        public int DbBatchSize { get; set; } = 2000;
        public string StageContainer { get; set; } = "stage";
        public string ProdContainer { get; set; } = "prod";
        public string MetadataFileName { get; set; } = "metadata.json";
    }
}
