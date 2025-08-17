using Polly;
using Polly.Retry;
using Azure;
using Npgsql;

namespace Worker.Resilience
{
    public class ResiliencePolicyFactory
    {
        public AsyncRetryPolicy DbRetryPolicy { get; }
        public AsyncRetryPolicy StorageRetry { get; }
        public AsyncRetryPolicy HttpRetry { get; }

        public ResiliencePolicyFactory()
        {
            DbRetryPolicy = Policy.Handle<NpgsqlException>()
                .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)));

            StorageRetry = Policy.Handle<RequestFailedException>()
                .WaitAndRetryAsync(5, i => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, i))));

            HttpRetry = Policy.Handle<HttpRequestException>()
                .WaitAndRetryAsync(4, i => TimeSpan.FromSeconds(Math.Pow(2, i)));
        }
    }
}
