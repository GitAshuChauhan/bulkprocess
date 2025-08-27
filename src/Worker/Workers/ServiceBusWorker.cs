using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
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
        private readonly ServiceBusProcessor _processor;
        private readonly IConfiguration _cfg;
        private readonly IDocumentRepository _repo;
        private readonly IZipUploader _uploader;
        private readonly IMetadataJobProcessor _jobProcessor;
        private readonly ResiliencePolicyFactory _policies;
        private readonly IJobLogger _jobLogger;
        private readonly BlobServiceClient _bsc;

        public ServiceBusWorker(ServiceBusClient sbClient, IConfiguration cfg, IDocumentRepository repo,
                                IZipUploader uploader, IMetadataJobProcessor jobProcessor, ResiliencePolicyFactory policies,
                                BlobServiceClient bsc, IJobLogger jobLogger)
        {
            _cfg = cfg; _repo = repo; _uploader = uploader; _jobProcessor = jobProcessor; _policies = policies; _bsc = bsc; _jobLogger = jobLogger;

            var queueName = cfg["ServiceBus:QueueName"];
            var maxRenewHours = int.TryParse(cfg["ServiceBus:MaxAutoRenewHours"], out var h) ? h : 6;

            _processor = sbClient.CreateProcessor(queueName, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1,
                MaxAutoLockRenewalDuration = TimeSpan.FromHours(maxRenewHours)
            });

            _processor.ProcessMessageAsync += OnMessageAsync;
            _processor.ProcessErrorAsync += OnErrorAsync;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _processor.StartProcessingAsync(stoppingToken);
            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
            finally { await _processor.StopProcessingAsync(stoppingToken); }
        }

        private async Task OnMessageAsync(ProcessMessageEventArgs args)
        {
            InboundMessage dto;
            try { dto = JsonSerializer.Deserialize<InboundMessage>(args.Message.Body.ToString())!; }
            catch (Exception jex)
            {
                //_logger.LogError(jex, "Malformed message; DLQ. Id={MessageId}", args.Message.MessageId);
                await _policies.ServiceBusRetryPolicy.ExecuteAsync(async () =>
                    await args.DeadLetterMessageAsync(args.Message, "InvalidMessage", jex.Message));
                return;
            }

            var correlationId = dto.FolderName;
            var job = await _repo.GetOrCreateMetadataJobAsync(correlationId, dto.MftPath, dto.Country, dto.AppName);

            try
            {
                //await _jobLogger.LogInfoAsync(job.Id, "Processing message for correlation {Correlation}", correlationId);
                await _jobLogger.LogJobStartAsync(job.Id, correlationId, dto.AppName, args.CancellationToken);

                //var zipBlobName = await _uploader.UploadZipFromMftAsync(job.Id, dto.MftPath, args.CancellationToken);
                var uploadResult = await _uploader.UploadZipFromMftAsync(job.Id, correlationId, dto.MftPath, args.CancellationToken);

                if (uploadResult.Skipped)
                {
                    await _jobLogger.LogInfoAsync(job.Id, $"ZIP already staged at {uploadResult.BlobName}, skipping upload.", args.CancellationToken);
                }
                else
                {
                    await _jobLogger.LogInfoAsync(job.Id, $"Uploaded ZIP to stage at {uploadResult.BlobName}.", args.CancellationToken);
                }

                var stage = _bsc.GetBlobContainerClient(_cfg["Storage:StageContainer"]);
                await _policies.BlobRetryPolicy.ExecuteAsync(async () => await stage.CreateIfNotExistsAsync(cancellationToken: args.CancellationToken));

                await _jobProcessor.RunAsync(job.Id, uploadResult.BlobName, dto.Country, dto.AppName, args.CancellationToken);

                await _policies.ServiceBusRetryPolicy.ExecuteAsync(async () => await args.CompleteMessageAsync(args.Message));
                
                await _jobLogger.LogJobCompletionAsync(job.Id, true, null, args.CancellationToken);

                await _jobLogger.LogInfoAsync(job.Id, "Message processing complete");
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Processing failed for Job {JobId}, Correlation {CorrelationId}. DeliveryCount={DeliveryCount}",
                //    job.Id, correlationId, args.Message.DeliveryCount);

                var maxDelivery = int.TryParse(_cfg["ServiceBus:MaxDeliveryCount"], out var md) ? md : 10;
                if (args.Message.DeliveryCount + 1 >= maxDelivery)
                {
                    //await _jobLogger.MarkJobFailedAsync(job.Id, $"Max deliveries exceeded: {ex.Message}");
                    await _repo.MarkJobFailedAsync(job.Id, ex.Message);

                    await _policies.ServiceBusRetryPolicy.ExecuteAsync(async () =>
                        await args.DeadLetterMessageAsync(args.Message, "MaxDeliveryExceeded", ex.Message));
                }
                else
                {
                    await _policies.ServiceBusRetryPolicy.ExecuteAsync(async () => await args.AbandonMessageAsync(args.Message));
                }

                await _jobLogger.LogJobCompletionAsync(job.Id, false, ex.Message, args.CancellationToken);
            }
        }

        private Task OnErrorAsync(ProcessErrorEventArgs e)
        {
           // _logger.LogError(e.Exception, "ServiceBus error Entity={Entity} Source={Source}", e.EntityPath, e.ErrorSource);

            // ServiceBus SDK errors (no job context) - platform logging can pick these up
            return Task.CompletedTask;
        }
    }
}
