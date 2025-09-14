using FluentFTP;
using FluentFTP;
using Worker.Abstractions;
using Worker.Infrastructure;

public class FtpMftClient : Worker.Abstractions.IFtpClient, IAsyncDisposable
{
    private readonly string _host;
    private readonly string _username;
    private readonly string _password;
    private readonly ResiliencePolicyFactory _policies;
    private AsyncFtpClient? _client;

    public FtpMftClient(string host, string username, string password, ResiliencePolicyFactory policies)
    {
        _host = host;
        _username = username;
        _password = password;
        _policies = policies;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _client = new AsyncFtpClient(_host, new System.Net.NetworkCredential(_username, _password));
        _client.ValidateCertificate += (c, e) => { e.Accept = true; };
        await _policies.FtpRetryPolicy.ExecuteAsync(() => _client.Connect(ct));
    }

    public async Task<bool> FileExistsAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        return await _policies.FtpRetryPolicy.ExecuteAsync(() => _client.FileExists(remotePath, ct));
    }

    public async Task<long> GetFileSizeAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        return await _policies.FtpRetryPolicy.ExecuteAsync(() => _client.GetFileSize(remotePath));
    }

    public async Task<Stream> OpenReadAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        return await _policies.FtpRetryPolicy.ExecuteAsync(() => _client.OpenRead(remotePath));
    }

    public async Task DownloadFileAsync(string remotePath, Stream destination, CancellationToken ct = default)
    {
        EnsureConnected();
        await _policies.FtpRetryPolicy.ExecuteAsync(() => _client.DownloadStream(destination, remotePath, token: ct));
    }

    private void EnsureConnected()
    {
        if (_client == null || !_client.IsConnected)
            throw new InvalidOperationException("FTP client is not connected. Call ConnectAsync first.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            await _client.Disconnect();
            await _client.DisposeAsync();
        }
    }
}
