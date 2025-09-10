using System;
using System.Threading.Tasks;

namespace SftpToAzure
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("--- SFTP to Azure Blob Storage Transfer Utility ---");

            // --- Configuration ---
            // Replace these placeholder values with your actual configuration.
            // For production, use a secure configuration provider (e.g., Azure Key Vault).
            var sftpConfig = new SftpConfig
            {
                Host = "sftp.example.com",
                Port = 22,
                Username = "your-sftp-username",
                Password = "your-sftp-password"
            };

            var azureBlobConfig = new AzureBlobConfig
            {
                ConnectionString = "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net",
                ContainerName = "your-blob-container-name"
            };

            var remoteFilePath = "/path/to/your/large-file.dat";
            var blobFileName = "uploaded-large-file.dat";

            // --- Choose Transfer Method ---
            // Set to 'true' to use the new high-performance parallel transfer.
            // Set to 'false' to use the original, memory-efficient single-stream transfer.
            bool useParallelTransfer = true;

            try
            {
                var uploader = new SftpToAzureBlobStreamingUploader(sftpConfig, azureBlobConfig);

                if (useParallelTransfer)
                {
                    Console.WriteLine("Starting transfer using PARALLEL method...");
                    // These are the settings for the parallel transfer.
                    // - maxDegreeOfParallelism: How many chunks to upload at the same time. 8 is a good starting point.
                    // - chunkSizeInMegabytes: The size of each chunk. Larger chunks are more efficient but use more memory. 20-100MB is typical.
                    await uploader.TransferFileInParallelAsync(
                        remoteFilePath,
                        blobFileName,
                        maxDegreeOfParallelism: 8,
                        chunkSizeInMegabytes: 50);
                }
                else
                {
                    Console.WriteLine("Starting transfer using SINGLE-STREAM method...");
                    await uploader.TransferFileAsync(remoteFilePath, blobFileName);
                }

                Console.WriteLine("--- Transfer completed successfully! ---");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"--- An error occurred: {ex.Message} ---");
                Console.WriteLine(ex.ToString()); // Print full exception for debugging
                Console.ResetColor();
            }
        }
    }
}
