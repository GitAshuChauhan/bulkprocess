using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace SftpToAzure
{
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
        /// Simple, low-memory single-stream transfer. Good for smaller files or when memory is highly constrained.
        /// </summary>
        public async Task TransferFileAsync(string remoteFilePath, string blobFileName)
        {
            using var sftpClient = new SftpClient(_sftpConfig.Host, _sftpConfig.Port, _sftpConfig.Username, _sftpConfig.Password);
            try
            {
                sftpClient.Connect();
                using var sftpStream = sftpClient.OpenRead(remoteFilePath);

                var blobServiceClient = new BlobServiceClient(_azureBlobConfig.ConnectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(_azureBlobConfig.ContainerName);
                await blobContainerClient.CreateIfNotExistsAsync();
                var blobClient = blobContainerClient.GetBlobClient(blobFileName);

                await blobClient.UploadAsync(sftpStream, overwrite: true);
            }
            finally
            {
                if (sftpClient.IsConnected) sftpClient.Disconnect();
            }
        }

        /// <summary>
        /// High-performance parallel file transfer. This method reads file chunks in parallel from the SFTP server
        /// and uploads them as blocks to Azure Blob Storage. It is the recommended approach for large files.
        /// </summary>
        public async Task TransferFileInParallelAsync(string remoteFilePath, string blobFileName, int maxDegreeOfParallelism = 8, int chunkSizeInMegabytes = 50)
        {
            var chunkSizeInBytes = chunkSizeInMegabytes * 1024 * 1024;

            // Get a client for the Azure Blob.
            var blobServiceClient = new BlobServiceClient(_azureBlobConfig.ConnectionString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(_azureBlobConfig.ContainerName);
            await blobContainerClient.CreateIfNotExistsAsync();
            var blobClient = blobContainerClient.GetBlobClient(blobFileName);

            // Use a single SFTP client for all operations. SftpClient is thread-safe.
            using var sftpClient = new SftpClient(_sftpConfig.Host, _sftpConfig.Port, _sftpConfig.Username, _sftpConfig.Password);

            try
            {
                sftpClient.Connect();
                Console.WriteLine("SFTP client connected.");

                // Get the file size to calculate chunks.
                var fileAttributes = sftpClient.GetAttributes(remoteFilePath);
                var fileSize = fileAttributes.Size;

                // Open the file on the SFTP server once to get a handle.
                var fileHandle = sftpClient.Open(remoteFilePath, FileMode.Open, FileAccess.Read);
                Console.WriteLine($"Remote file opened. Handle obtained. File size: {fileSize / (1024 * 1024)} MB");

                try
                {
                    var numChunks = (int)Math.Ceiling((double)fileSize / chunkSizeInBytes);
                    var blockIds = new ConcurrentDictionary<int, string>();
                    var uploadTasks = new List<Task>(numChunks);

                    // Use a semaphore to limit the number of concurrent tasks.
                    using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

                    Console.WriteLine($"Starting parallel upload in {numChunks} chunks with up to {maxDegreeOfParallelism} workers...");

                    for (int i = 0; i < numChunks; i++)
                    {
                        await semaphore.WaitAsync(); // Wait for an available slot.

                        var chunkIndex = i;

                        uploadTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var offset = (long)chunkIndex * chunkSizeInBytes;
                                var length = (int)Math.Min(chunkSizeInBytes, fileSize - offset);

                                // This is the key operation: Read a specific chunk directly using the file handle.
                                var buffer = sftpClient.ReadBytes(fileHandle, (ulong)offset, length);

                                using var memoryStream = new MemoryStream(buffer);

                                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkIndex.ToString("d6")));
                                await blobClient.StageBlockAsync(blockId, memoryStream);
                                blockIds.TryAdd(chunkIndex, blockId);
                                Console.WriteLine($"  - Chunk {chunkIndex + 1}/{numChunks} (Size: {length / 1024} KB) staged successfully.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR] Failed to process chunk {chunkIndex + 1}: {ex.Message}");
                                throw; // Rethrow to fail the Task.WhenAll
                            }
                            finally
                            {
                                semaphore.Release(); // Release the slot.
                            }
                        }));
                    }

                    // Wait for all staging tasks to complete.
                    await Task.WhenAll(uploadTasks);

                    // Commit the staged blocks in the correct order to finalize the blob.
                    if (blockIds.Count == numChunks)
                    {
                        Console.WriteLine("All chunks staged. Committing block list...");
                        var sortedBlockIds = blockIds.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value);
                        await blobClient.CommitBlockListAsync(sortedBlockIds);
                        Console.WriteLine("Parallel upload complete!");
                    }
                    else
                    {
                        throw new Exception("Upload failed: Not all chunks were successfully staged.");
                    }
                }
                finally
                {
                    // Ensure the file handle is closed.
                    sftpClient.Close(fileHandle);
                    Console.WriteLine("Remote file handle closed.");
                }
            }
            finally
            {
                // Ensure the client is disconnected.
                if (sftpClient.IsConnected)
                {
                    sftpClient.Disconnect();
                    Console.WriteLine("SFTP client disconnected.");
                }
            }
        }
    }

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
