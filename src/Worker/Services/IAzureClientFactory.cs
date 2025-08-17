using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;

namespace Worker.Services
{
    public interface IAzureClientFactory
    {
        ServiceBusClient CreateServiceBusClient();
        BlobServiceClient CreateBlobServiceClient();
    }
}
