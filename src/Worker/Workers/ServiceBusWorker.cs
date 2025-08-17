using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Worker.Configuration;
using Worker.Data.Repositories;
using Worker.Pipeline;
using Worker.Services;

namespace Worker.Workers
{
    public class ServiceBusWorker : BackgroundService
    {
        private readonly IAzureClientFactory _af;
        private readonly ILogger<ServiceBusWorker> _log;
        private readonly ServiceBusOptions _opts;
        private readonly IDocumentRepository _repo;
        private readonly ZipIngestPipeline _pipeline;

        private ServiceBusClient? _client;
        private ServiceBusReceiver? _receiver;

        public ServiceBusWorker(IAzureClientFactory af, ILogger<ServiceBusWorker> log, IOptions<ServiceBusOptions> opts, IDocumentRepository repo, ZipIngestPipeline pipeline)
        {
            _af = af; _log = log; _opts = opts.Value; _repo = repo; _pipeline = pipeline;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _client = _af.CreateServiceBusClient();
            _receiver = _client.CreateReceiver(_opts.QueueName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("ServiceBusWorker started, listening to {Queue}", _opts.QueueName);

            while (!stoppingToken.IsCancellationRequested)
            {
                ServiceBusReceivedMessage? msg = null;
                try
                {
                    msg = await _receiver!.ReceiveMessageAsync(TimeSpan.FromSeconds(20), stoppingToken);
                    if (msg == null) { await Task.Delay(1000, stoppingToken); continue; }

                    var body = msg.Body.ToString();
                    // Expect a simple JSON: { "mftZipPath": "path/to/zip.zip" }
                    var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (!doc.RootElement.TryGetProperty("mftZipPath", out var p))
                    {
                        _log.LogWarning("Message missing mftZipPath; dead-lettering");
                        await _receiver.DeadLetterMessageAsync(msg, cancellationToken: stoppingToken);
                        continue;
                    }

                    var mftZipPath = p.GetString()!;
                    _log.LogInformation("Received message for zip {Path}", mftZipPath);

                    // 1) create job row
                    var job = await _repo.CreateJobAsync(mftZipPath, stoppingToken);

                    // 2) run pipeline (upload zip, extract, stage metadata, process docs)
                    await _pipeline.RunAsync(mftZipPath, job.Id, stoppingToken);

                    // 3) complete message after job done
                    await _receiver.CompleteMessageAsync(msg, stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error processing Service Bus message");
                    if (msg != null)
                    {
                        try { await _receiver.AbandonMessageAsync(msg, cancellationToken: stoppingToken); } catch { }
                    }
                }
            }
        }
    }
}
