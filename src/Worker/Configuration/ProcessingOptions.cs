
namespace Worker.Configuration
{
    public class ProcessingOptions
    {
        public int MaxDegreeOfParallelism { get; set; } = 32;
        public int DbBatchSize { get; set; } = 1000;
    }
}