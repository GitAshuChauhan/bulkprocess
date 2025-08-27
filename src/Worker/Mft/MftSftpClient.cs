using Renci.SshNet;
using System;
using System.Threading;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Infrastructure;

namespace Worker.Mft
{
    public class MftSftpClient : IMftClient, IAsyncDisposable
    {
        private readonly IConfiguration _cfg;
        private readonly ResiliencePolicyFactory _policies;
        private SftpClient? _client;

        public MftSftpClient(IConfiguration cfg, ResiliencePolicyFactory policies)
        {
            _cfg = cfg; _policies = policies;
        }

        private SftpClient Ensure()
        {
            if (_client != null && _client.IsConnected) return _client;
            _client?.Dispose();
            _client = new SftpClient(_cfg["Sftp:Host"]!, int.Parse(_cfg["Sftp:Port"]!), _cfg["Sftp:Username"]!, _cfg["Sftp:Password"]!);
            _policies.SftpRetryPolicy.Execute(() => _client.Connect());
            return _client;
        }

        public async Task<Stream> OpenReadAsync(string remotePath, CancellationToken ct = default)
        {
            var client = Ensure();
            return await Task.Run(() => client.OpenRead(remotePath), ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            _client?.Dispose();
        }
    }
}
