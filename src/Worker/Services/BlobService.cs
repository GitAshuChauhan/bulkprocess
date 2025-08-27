using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Data.Entities;
using Worker.Infrastructure;
using Worker.Telemetry;

namespace Worker.Services
{
    public class BlobService : IBlobService
    {
        private readonly BlobContainerClient _stage;
        private readonly BlobContainerClient _prod;
        private readonly IProductionRepository _prodRepo;
        private readonly ResiliencePolicyFactory _policies;
        private readonly ILogger<BlobService> _logger;

        public BlobService(BlobServiceClient storage, IProductionRepository prodRepo, ResiliencePolicyFactory policies, ILogger<BlobService> logger)
        {
            _stage = storage.GetBlobContainerClient("stage");
            _prod = storage.GetBlobContainerClient("prod");
            _prodRepo = prodRepo;
            _policies = policies;
            _logger = logger;
        }

        public async Task PromoteAsync(DocumentEntity doc, Guid jobId, string country, string appName, CancellationToken ct)
        {
            using var act = Telemetry.Source.StartActivity("PromoteToProd");
            var src = _stage.GetBlobClient(doc.Filepath);
            var dest = _prod.GetBlobClient(doc.Filepath);

            await _policies.BlobRetryPolicy.ExecuteAsync(async () =>
            {
                await using var s = await src.OpenReadAsync(cancellationToken: ct);
                await dest.UploadAsync(s, overwrite: true, cancellationToken: ct);
            });

            // Build tags dictionary from TagsJson (list of objects => flatten)
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(doc.TagsJson))
            {
                var list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(doc.TagsJson) ?? new();
                foreach (var kv in list.SelectMany(x => x))
                    if (!tags.ContainsKey(kv.Key)) tags[kv.Key] = kv.Value;
            }

            if (tags.Count > 0)
            {
                await _policies.BlobRetryPolicy.ExecuteAsync(async () => await dest.SetTagsAsync(tags, cancellationToken: ct));
            }

            var prodDoc = new ProductionDocument
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                FileGuid = doc.FileGuid,
                FileName = Path.GetFileName(doc.Filepath),
                Extension = doc.Extension,
                BlobPath = dest.Uri.ToString()
            };

            await _prodRepo.AddDocumentWithTagsAsync(prodDoc, tags, ct);

            await _policies.BlobRetryPolicy.ExecuteAsync(async () => await src.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct));

            _logger.LogInformation("Promoted {Blob}", doc.Filepath);
        }
    }
}
