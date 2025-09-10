using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Renci.SshNet;

namespace SftpToAzure
{
    /// <summary>
    /// A service to efficiently stream a file from an SFTP server to Azure Blob Storage.
    /// </summary>
    public class SftpToAzureBlobStreamingUploader
    {
        private readonly SftpConfig _sftpConfig;
        private readonly AzureBlobConfig _azureBlobConfig;

        public SftpToAzureBlobStreamingUploader(SftpConfig sftpConfig, AzureBlobConfig azureBlobConfig)
        {
            _sftpConfig = sftpConfig ?? throw new ArgumentNullException(nameof(sftpConfig));
            _azureBlobConfig = azureBlobConfig ?? throw new ArgumentNullException(nameof(azureBlobConfig));
        }

        /// <summary>
        /// Downloads a file from the SFTP server and uploads it to Azure Blob Storage as a stream.
        /// This method is highly memory-efficient as it does not load the entire file into memory.
        /// </summary>
        /// <param name="remoteFilePath">The full path to the file on the SFTP server.</param>
        /// <param name="blobFileName">The name of the blob to create in Azure Storage.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public async Task TransferFileAsync(string remoteFilePath, string blobFileName)
        {
            // Establish a connection to the SFTP server.
            // The 'using' statement ensures the connection is properly closed.
            using var sftpClient = new SftpClient(_sftpConfig.Host, _sftpConfig.Port, _sftpConfig.Username, _sftpConfig.Password);

            try
            {
                Console.WriteLine("Connecting to SFTP server...");
                sftpClient.Connect();
                Console.WriteLine("Connected to SFTP server.");

                // Open a read stream to the remote file. This does not download the file yet.
                // It provides a stream that we can read from on-demand.
                using var sftpStream = sftpClient.OpenRead(remoteFilePath);
                Console.WriteLine($"Opened read stream to remote file: {remoteFilePath}");

                // Get a client for the specific blob in Azure Storage.
                var blobServiceClient = new BlobServiceClient(_azureBlobConfig.ConnectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(_azureBlobConfig.ContainerName);

                // Ensure the container exists.
                await blobContainerClient.CreateIfNotExistsAsync();

                var blobClient = blobContainerClient.GetBlobClient(blobFileName);
                Console.WriteLine($"Got BlobClient for blob: {blobFileName}");

                Console.WriteLine("Starting upload to Azure Blob Storage...");

                // This is the key part of the operation.
                // We pass the SFTP stream directly to the Azure Blob Storage SDK's UploadAsync method.
                // The SDK intelligently reads from the source stream in chunks and uploads them as blocks
                // to Azure Storage, without ever loading the entire 10 GB file into local memory.
                // The `overwrite: true` parameter will replace the blob if it already exists.
                await blobClient.UploadAsync(sftpStream, new BlobHttpHeaders { ContentType = "application/octet-stream" }, conditions: null);

                Console.WriteLine("Upload complete.");
            }
            catch (Exception ex)
            {
                // Log the exception details for troubleshooting.
                Console.WriteLine($"An error occurred during the transfer: {ex.Message}");
                Console.WriteLine(ex.ToString());
                throw;
            }
            finally
            {
                // Disconnect from the SFTP server if the connection is open.
                if (sftpClient.IsConnected)
                {
                    sftpClient.Disconnect();
                    Console.WriteLine("Disconnected from SFTP server.");
                }
            }
        }
    }

    // Configuration classes to hold connection details.
    public class SftpConfig
    {
        public string Host { get; set; }
        public int Port { get; set; } = 22;
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class AzureBlobConfig
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
    }
}
