using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface IMftClient : IAsyncDisposable
    {
        Task<Stream> OpenReadAsync(string remotePath, CancellationToken ct = default);
    }
}
