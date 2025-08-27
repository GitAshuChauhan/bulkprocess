using Azure.Storage.Blobs;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Data.Entities;
using Worker.Infrastructure;

namespace Worker.Services
{
    public class MetadataStager : IMetadataStager
    {
        private readonly IDocumentRepository _repo;
        //private readonly ILogger<MetadataStager> _logger;
        private readonly IJobLogger _jobLogger;

        public MetadataStager(IDocumentRepository repo, JobLogger jobLogger)
        { _repo = repo; 
            //_logger = logger; 
            _jobLogger = jobLogger; }

        public async Task StageMetadataAsync(Guid jobId, BlobClient metadataBlob, CancellationToken ct)
        {
            //var jobId = job.Id;
            //_logger.LogInformation("Staging metadata for job {JobId}", jobId);
            await _jobLogger.LogInfoAsync(jobId, "Staging metadata from blob {Blob}", metadataBlob.Name);

            using var stream = await metadataBlob.OpenReadAsync(cancellationToken: ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var toAdd = new List<DocumentEntity>();
            foreach (var dt in root.GetProperty("doctypes").EnumerateArray())
            {
                foreach (var d in dt.GetProperty("documents").EnumerateArray())
                {
                    var filepath = d.GetProperty("filepath").GetString()!;
                    var fileguid = d.GetProperty("fileguid").GetString()!;
                    var extension = d.GetProperty("extension").GetString()!;

                    toAdd.Add(new DocumentEntity
                    {
                        Id = Guid.NewGuid(),
                        JobId = jobId,
                        FileGuid = fileguid,
                        Filepath = filepath.Replace("\\", "/"),
                        Extension = extension,
                        Status = DocumentStatus.Pending
                    });
                }
            }
            await _repo.AddDocumentsAsync(toAdd);
            await _jobLogger.LogInfoAsync(jobId, "Staged {Count} documents", toAdd.Count);
        }
    }
}
