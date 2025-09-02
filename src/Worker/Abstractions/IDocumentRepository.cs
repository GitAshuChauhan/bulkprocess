//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Worker.Data.Entities;

//namespace Worker.Abstractions
//{
//    public interface IDocumentRepository
//    {
//        Task<MetadataJob> GetOrCreateJobAsync(Guid correlationId, string? sourcePath = null, CancellationToken ct = default);
//        //Task<MetadataJob> GetOrCreateMetadataJobAsync(string correlationId, string sourcePath, string country, string appName);
//        Task SetJobStartedAsync(Guid jobId, CancellationToken ct = default);
//        Task SetJobCompletedAsync(Guid jobId, int success, int failed, string? reason = null, CancellationToken ct = default);
//        Task SetJobFailedAsync(Guid jobId, string reason, CancellationToken ct = default);

//        Task<IReadOnlyList<DocumentStagingRaw>> GetPendingStagingRowsAsync(Guid jobId, int take, CancellationToken ct = default);
//        Task<int> CountPendingByJobAsync(Guid jobId, CancellationToken ct = default);
//        Task UpdateStagingRowStatusAsync(Guid stagingId, string status, string? error, CancellationToken ct = default);
//        Task SaveProductionDocumentAsync(ProductionDocumentEntity prod, IEnumerable<ProductionDocumentTag> tags, CancellationToken ct = default);
//    }
//}
