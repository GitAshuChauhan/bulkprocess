using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Abstractions;
using Worker.Infrastructure;

namespace Worker.Mft
{
    public class SftpMftClient : IMftClient
    {
        private readonly IConfiguration _cfg;
        private readonly ResiliencePolicyFactory _policies;
        private readonly ILogger<SftpMftClient> _logger;

        public SftpMftClient(IConfiguration cfg, ResiliencePolicyFactory policies, ILogger<SftpMftClient> logger)
        {
            _cfg = cfg; _policies = policies; _logger = logger;
        }

        private ConnectionInfo BuildConnectionInfo()
        {
            var host = _cfg["Sftp:Host"] ?? throw new InvalidOperationException("Sftp:Host is required");
            var port = int.TryParse(_cfg["Sftp:Port"], out var p) ? p : 22;
            var username = _cfg["Sftp:Username"] ?? throw new InvalidOperationException("Sftp:Username is required");
            var password = _cfg["Sftp:Password"];
            var privateKey = _cfg["Sftp:PrivateKeyPath"];

            if (!string.IsNullOrWhiteSpace(privateKey))
            {
                var pk = new PrivateKeyFile(privateKey);
                return new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, pk));
            }
            if (!string.IsNullOrWhiteSpace(password))
            {
                return new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password));
            }
            throw new InvalidOperationException("SFTP credentials missing");
        }

        public async Task<bool> ExistsAsync(string remotePath, CancellationToken ct = default)
        {
            return await _policies.SftpRetryPolicy.ExecuteAsync(async token =>
            {
                return await Task.Run(() =>
                {
                    using var client = new SftpClient(BuildConnectionInfo());
                    client.Connect();
                    var exists = client.Exists(remotePath);
                    client.Disconnect();
                    return exists;
                }, token);
            }, ct);
        }

        public async Task<Stream> OpenReadAsync(string remotePath, CancellationToken ct = default)
        {
            return await _policies.SftpRetryPolicy.ExecuteAsync(async token =>
            {
                return await Task.Run(() =>
                {
                    var client = new SftpClient(BuildConnectionInfo());
                    client.Connect();
                    if (!client.Exists(remotePath))
                    {
                        try { client.Disconnect(); } catch { }
                        client.Dispose();
                        throw new FileNotFoundException($"Remote not found: {remotePath}");
                    }
                    var inner = client.OpenRead(remotePath);
                    return (Stream)new CompositeSftpStream(inner, client, _logger);
                }, token);
            }, ct);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class CompositeSftpStream : Stream
        {
            private readonly Stream _inner;
            private readonly SftpClient _client;
            private readonly ILogger _logger;
            private bool _disposed;

            public CompositeSftpStream(Stream inner, SftpClient client, ILogger logger)
            {
                _inner = inner; _client = client; _logger = logger;
                _disposed = false;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => await _inner.ReadAsync(buffer, cancellationToken);

            protected override void Dispose(bool disposing)
            {
                if (_disposed) return;
                if (disposing)
                {
                    try { _inner.Dispose(); } catch (Exception) { }
                    try { if (_client.IsConnected) _client.Disconnect(); } catch (Exception) { }
                    try { _client.Dispose(); } catch (Exception) { }
                }
                _disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
