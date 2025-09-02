using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Data.Entities.production;

namespace Worker.Data.Repositories
{
    public interface IProductionRepository
    {
        //Task SaveDocumentWithTagsAsync(ProductionDocumentEntity doc, IEnumerable<ProductionDocumentTag> tags, CancellationToken ct);
        Task SaveProductionDocumentAsync(ProductionDocumentEntity prod, IEnumerable<ProductionDocumentTag> tags, CancellationToken ct = default);
    }
}
