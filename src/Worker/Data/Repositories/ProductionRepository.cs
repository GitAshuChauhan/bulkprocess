using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Data.DbContext;
using Worker.Data.Entities.production;
using Worker.Infrastructure;

namespace Worker.Data.Repositories
{
    public class ProductionRepository : IProductionRepository
    {
        private readonly ProductionDbContext _ctx;
        private readonly ResiliencePolicyFactory _policies;
        private readonly ILogger<StagingRepository> _logger;
        //public ProductionRepository(ProductionDbContext db) => _db = db;

        public ProductionRepository(ProductionDbContext ctx, ResiliencePolicyFactory policies, ILogger<StagingRepository> logger)
        {
            _ctx = ctx; _policies = policies; _logger = logger;
        }
        public async Task SaveProductionDocumentAsync(ProductionDocumentEntity doc, IEnumerable<ProductionDocumentTag> tags, CancellationToken ct)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                var exists = await _ctx.ProductionDocuments.AnyAsync(p => p.JobId == doc.JobId && p.FileGuid == doc.FileGuid, ct);
                if (exists) return;
                await _ctx.ProductionDocuments.AddAsync(doc, ct);
                if (tags != null && tags.Any()) await _ctx.ProductionDocumentTags.AddRangeAsync(tags, ct);
                await _ctx.SaveChangesAsync(ct);
            });
        }
    }
}
