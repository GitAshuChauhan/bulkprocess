using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Worker.Configuration;

namespace Worker.Services
{
    public class AzureClientFactory : IAzureClientFactory
    {
        private readonly IConfiguration _cfg;
        private readonly TokenCredential? _credential;
        private readonly string? _sbConn;
        private readonly string? _blobConn;

        public AzureClientFactory(IConfiguration cfg)
        {
            _cfg = cfg;
            var mode = cfg["Auth:Mode"] ?? "managedIdentity";
            if (mode.Equals("managedIdentity", StringComparison.OrdinalIgnoreCase))
            {
                _credential = new DefaultAzureCredential();
            }
            _sbConn = cfg.GetConnectionString("ServiceBus");
            _blobConn = cfg.GetConnectionString("BlobStorage");
        }

        public ServiceBusClient CreateServiceBusClient()
        {
            var mode = _cfg["Auth:Mode"] ?? "managedIdentity";
            if (mode.Equals("managedIdentity", StringComparison.OrdinalIgnoreCase))
            {
                var ns = _cfg["ServiceBus:NamespaceFqdn"] ?? throw new InvalidOperationException("ServiceBus:NamespaceFqdn required");
                return new ServiceBusClient(ns, _credential!);
            }
            return new ServiceBusClient(_sbConn!);
        }

        public BlobServiceClient CreateBlobServiceClient()
        {
            var mode = _cfg["Auth:Mode"] ?? "managedIdentity";
            if (mode.Equals("managedIdentity", StringComparison.OrdinalIgnoreCase))
            {
                var accountUrl = _cfg["Auth:StorageAccountUrl"] ?? _cfg["Auth:StorageAccountUrl"];
                if (string.IsNullOrEmpty(accountUrl)) throw new InvalidOperationException("Storage account URL required for managedIdentity");
                return new BlobServiceClient(new Uri(accountUrl), _credential!);
            }
            return new BlobServiceClient(_blobConn!);
        }
    }
}
