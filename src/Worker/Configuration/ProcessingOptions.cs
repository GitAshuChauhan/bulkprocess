
namespace Worker.Configuration
{
    public class ProcessingOptions
    {
        public int MaxDegreeOfParallelism { get; set; } = 32;//System.Environment.ProcessorCount;//[ASHU: TODO to set the idle value]
        public int DbBatchSize { get; set; } = 1000;
    }
}