using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Services
{
    public interface IBlobMover
    {
        Task MoveWithTagsAsync(string stageContainer, string prodContainer, string blobName, IDictionary<string, string> tags, CancellationToken ct);
    }
}
