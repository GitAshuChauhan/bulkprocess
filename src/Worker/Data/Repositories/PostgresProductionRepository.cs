using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Data.Entities;
using Worker.Infrastructure;

namespace Worker.Data.Repositories
{
    public class PostgresProductionRepository : IProductionRepository
    {
        private readonly DataContext _ctx;
        private readonly ResiliencePolicyFactory _policies;

        public PostgresProductionRepository(DataContext ctx, ResiliencePolicyFactory policies)
        {
            _ctx = ctx; _policies = policies;
        }

        public async Task AddDocumentWithTagsAsync(ProductionDocument doc, IDictionary<string, string> tags, CancellationToken ct)
        {
            await _policies.DbRetryPolicy.ExecuteAsync(async () =>
            {
                foreach (var kv in tags)
                {
                    doc.Tags.Add(new ProductionDocumentTag
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = doc.Id,
                        Key = kv.Key,
                        Value = kv.Value
                    });
                }

                _ctx.ProductionDocuments.Add(doc);
                await _ctx.SaveChangesAsync(ct);
            });
        }
    }
}
