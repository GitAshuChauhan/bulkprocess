using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Services
{
    public interface IMftClient
    {
        Task<Stream> DownloadAsync(string path, CancellationToken ct);
    }
}
