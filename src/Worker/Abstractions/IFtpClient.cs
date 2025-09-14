using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Abstractions
{
    public interface IFtpClient : IAsyncDisposable
    {
        Task ConnectAsync(CancellationToken ct = default);
        Task<bool> FileExistsAsync(string remotePath, CancellationToken ct = default);
        Task<long> GetFileSizeAsync(string remotePath, CancellationToken ct = default);
        Task<Stream> OpenReadAsync(string remotePath, CancellationToken ct = default);
        Task DownloadFileAsync(string remotePath, Stream destination, CancellationToken ct = default);
    }

}
