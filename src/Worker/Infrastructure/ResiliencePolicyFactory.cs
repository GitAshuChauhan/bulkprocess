using Azure;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Renci.SshNet.Common;
using System;

namespace Worker.Infrastructure
{
    public class ResiliencePolicyFactory
    {
        public IAsyncPolicy DbRetryPolicy { get; }
        public IAsyncPolicy BlobRetryPolicy { get; }
        public IAsyncPolicy ServiceBusRetryPolicy { get; }
        public IAsyncPolicy SftpRetryPolicy { get; }

        public ResiliencePolicyFactory(ILogger<ResiliencePolicyFactory> logger)
        {
            var delays = Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(1), retryCount: 5, fastFirst: true);

            DbRetryPolicy = Policy.Handle<TimeoutException>()
                .Or<InvalidOperationException>(ex => ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
                .WaitAndRetryAsync(delays, (ex, ts, r, ctx) => logger.LogWarning(ex, "DB transient, retry {Retry} after {Delay}", r, ts));

            BlobRetryPolicy = Policy.Handle<RequestFailedException>(ex => ex.Status == 429 || ex.Status == 503)
                .WaitAndRetryAsync(delays, (ex, ts, r, ctx) => logger.LogWarning(ex, "Blob transient, retry {Retry} after {Delay}", r, ts));

            ServiceBusRetryPolicy = Policy.Handle<ServiceBusException>(ex => ex.IsTransient)
                .WaitAndRetryAsync(delays, (ex, ts, r, ctx) => logger.LogWarning(ex, "ServiceBus transient, retry {Retry} after {Delay}", r, ts));

            SftpRetryPolicy = Policy.Handle<SshException>()
                .Or<SftpPathNotFoundException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(delays, (ex, ts, r, ctx) => logger.LogWarning(ex, "SFTP transient, retry {Retry} after {Delay}", r, ts));
        }
    }
}
