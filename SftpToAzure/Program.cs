using System;
using System.Threading.Tasks;

namespace SftpToAzure
{
    class Program
    {
        /// <summary>
        /// Main entry point for the application.
        /// Configures and runs the SFTP to Azure Blob Storage transfer.
        /// </summary>
        static async Task Main(string[] args)
        {
            Console.WriteLine("--- SFTP to Azure Blob Storage Streaming Transfer ---");

            // --- IMPORTANT ---
            // Replace these placeholder values with your actual configuration.
            // For production applications, use a secure configuration provider
            // like Azure Key Vault, appsettings.json, or environment variables.

            var sftpConfig = new SftpConfig
            {
                Host = "sftp.example.com",
                Port = 22,
                Username = "your-sftp-username",
                Password = "your-sftp-password"
            };

            var azureBlobConfig = new AzureBlobConfig
            {
                // It's highly recommended to use a connection string from a secure source.
                ConnectionString = "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net",
                ContainerName = "your-blob-container-name"
            };

            // The full path to the source file on the SFTP server.
            var remoteFilePath = "/path/to/your/10gb-file.dat";

            // The desired name for the file once it's in Azure Blob Storage.
            var blobFileName = "uploaded-file.dat";

            try
            {
                Console.WriteLine("Initializing transfer service...");
                var uploader = new SftpToAzureBlobStreamingUploader(sftpConfig, azureBlobConfig);

                Console.WriteLine($"Starting transfer of '{remoteFilePath}' to blob '{blobFileName}'...");
                await uploader.TransferFileAsync(remoteFilePath, blobFileName);

                Console.WriteLine("--- Transfer completed successfully! ---");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"--- An error occurred: {ex.Message} ---");
                // The detailed exception is already printed by the uploader class.
                // For a production app, you would log this to your logging system.
                Console.ResetColor();
            }
        }
    }
}
