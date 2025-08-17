using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Worker.Services
{
    public class HttpMftClient : IMftClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public HttpMftClient(IConfiguration cfg, IHttpClientFactory factory)
        {
            _baseUrl = cfg["Mft:BaseUrl"] ?? "";
            _http = factory.CreateClient("mft");
        }

        public async Task<Stream> DownloadAsync(string path, CancellationToken ct)
        {
            var url = new Uri(new Uri(_baseUrl.TrimEnd('/') + "/"), path);
            var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStreamAsync(ct);
        }
    }
}
