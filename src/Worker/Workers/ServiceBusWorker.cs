using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Npgsql.Replication.PgOutput.Messages;
using System.Text.Json;
using Worker.Abstractions;
using Worker.Configuration;
using Worker.Data.Entities;
using Worker.Data.Repositories;
using Worker.Infrastructure;
using Worker.Services;

namespace Worker.Workers
{
    public class ServiceBusWorker : BackgroundService
    {
        private readonly ServiceBusClient _client;
        private readonly IZipHandler _ziphandler;
        private readonly ICsvStager _csvStager;
        private readonly IStagingRepository _stagingRepo;
        private readonly ILogger<ServiceBusWorker> _logger;
        private readonly IJobLogger _jobLogger;
        private readonly ResiliencePolicyFactory _policies;

        private readonly IJobAlertService _alerts;
        private readonly IDocumentProcessor _docprocessor;
        private ServiceBusProcessor _sbprocessorClient;
        
        public ServiceBusWorker(ServiceBusClient client, ServiceBusProcessor sbprocessor, ICsvStager csvStager,IJobLogger joblogger, ILogger<ServiceBusWorker> logger, IZipHandler ziphandler, IStagingRepository stagingrepo, IJobAlertService alerts, IDocumentProcessor processor, ResiliencePolicyFactory policies)
        {
            _client = client; 
            _sbprocessorClient = sbprocessor; 
            _csvStager = csvStager; 
            _jobLogger = joblogger; 
            _logger = logger;
            _ziphandler = ziphandler;
            _stagingRepo = stagingrepo; 
            _alerts = alerts; 
            _docprocessor = processor;
            _policies = policies;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //var options = new ServiceBusProcessorOptions
            //{
            //    MaxConcurrentCalls = 1,
            //    MaxAutoLockRenewalDuration = TimeSpan.FromHours(4),
            //    AutoCompleteMessages = false
            //};
           // _sbprocessorClient = _client.CreateProcessor(queue, options);
            _sbprocessorClient.ProcessMessageAsync += OnMessageAsync;
            _sbprocessorClient.ProcessErrorAsync += args => { _logger.LogError(args.Exception, "SB Error"); return Task.CompletedTask; };
            await _sbprocessorClient.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("ServiceBusProcessor started");
        }

        private async Task OnMessageAsync(ProcessMessageEventArgs args)
        {
            var ct = args.CancellationToken;
            IngestMessage? msg;
            try
            {
                msg = JsonSerializer.Deserialize<IngestMessage>(args.Message.Body.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                if (msg == null) throw new Exception("invalid message");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid message - deadletter");
                await args.DeadLetterMessageAsync(args.Message, "InvalidMessage", ex.Message, ct);
                return;
            }

            var job = await _stagingRepo.GetOrCreateJobAsync(msg.CorrelationId, msg.MftZipPath, ct);
            await _stagingRepo.SetJobStartedAsync(job.Id, ct);
            _alerts.TrackJobStarted(job.Id, msg.ClientId);

            try
            {
                var upload = await _ziphandler.UploadZipFromMftAsync(job.Id, msg.CorrelationId, msg.MftZipPath, ct);
                if (!upload.Skipped)
                {
                    _logger.LogInformation("Zip uploaded");
                }

                await using var csvStream = await _ziphandler.StageZipAndExtractCsvAsync(job, msg.CorrelationId, ct);

                // Level-1 header validation & stage rows
                await _csvStager.StageCsvAsync(csvStream, job.Id, args.CancellationToken);

                // Process job documents in parallel inside the pod
                await _docprocessor.ProcessJobAsync(job.Id, args.CancellationToken);

                // mark complete (placeholder)
                //await _repo.SetJobCompletedAsync(job.Id, 0, 0, null, ct);
                _alerts.TrackJobCompleted(job.Id, true);
                await _policies.ServiceBusRetryPolicy.ExecuteAsync(async () => await args.CompleteMessageAsync(args.Message, ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Processing failed");
                _alerts.TrackJobCompleted(job.Id, false, ex.Message);

                // Let built-in MaxDelivery count handle redelivery; if exceeded, SB will move to DLQ
                //by defuault max delivery count is 10.We can change it in the azure portal.
                await _policies.ServiceBusRetryPolicy.ExecuteAsync(async () => await args.AbandonMessageAsync(args.Message, cancellationToken: ct));
            }
        }      

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_sbprocessorClient != null)
            {
                await _sbprocessorClient.StopProcessingAsync(cancellationToken);
                await _sbprocessorClient.DisposeAsync();
            }
            await base.StopAsync(cancellationToken);
        }

        private sealed record IngestMessage(Guid CorrelationId, string MftZipPath, string? ClientId);
    }
}
