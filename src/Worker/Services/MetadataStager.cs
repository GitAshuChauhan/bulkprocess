using System.Text.Json;
using Microsoft.Extensions.Options;
using Worker.Configuration;
using Worker.Data;
using Worker.Data.Repositories;

namespace Worker.Services
{
    public class MetadataStager : IMetadataStager
    {
        private readonly IDocumentRepository _repo;
        private readonly DocumentProcessingOptions _opts;
        private readonly ILogger<MetadataStager> _log;

        public MetadataStager(IDocumentRepository repo, IOptions<DocumentProcessingOptions> opts, ILogger<MetadataStager> log)
        {
            _repo = repo;
            _opts = opts.Value;
            _log = log;
        }

        /// <summary>
        /// Parse metadata JSON via streaming reader and bulk insert DocumentEntity rows.
        /// Expecting JSON structure like:
        /// { "country": "...", "appname": "...", "doctypes": [ { "doctype": "...", "documents": [ { "filepath":"...","fileguid":"...","extension":"pdf" } ] } ] }
        /// </summary>
        public async Task StageMetadataAsync(Guid jobId, Stream metadataJsonStream, CancellationToken ct)
        {
            var docs = new List<DocumentEntity>();
            var readerState = new JsonReaderState();
            var buffer = new byte[64 * 1024];
            int bytesInBuffer = 0;
            long seq = 0;
            string currentDoctype = null;
            string country = null;
            string appname = null;

            while (true)
            {
                int read = await metadataJsonStream.ReadAsync(buffer, bytesInBuffer, buffer.Length - bytesInBuffer, ct);
                if (read == 0 && bytesInBuffer == 0) break;
                var span = new ReadOnlySpan<byte>(buffer, 0, bytesInBuffer + read);
                var reader = new Utf8JsonReader(span, read == 0, readerState);

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var name = reader.GetString();
                        if (string.Equals(name, "country", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            country = reader.GetString();
                        }
                        else if (string.Equals(name, "appname", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            appname = reader.GetString();
                        }
                        else if (string.Equals(name, "doctype", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            currentDoctype = reader.GetString();
                        }
                        else if (string.Equals(name, "documents", StringComparison.OrdinalIgnoreCase))
                        {
                            // skip to array start
                        }
                        else if (string.Equals(name, "filepath", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            var fp = reader.GetString();
                            // next properties will include fileguid and extension — parse them manually from the surrounding tokens
                            // To simplify, assume the object is: { "filepath":"...","fileguid":"...","extension":"..." }
                            // We'll read fileguid and extension by advancing until we hit them
                            string fg = null; string ext = null;
                            // reader is currently at the string value of filepath; continue reading inside same object
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                            {
                                if (reader.TokenType == JsonTokenType.PropertyName)
                                {
                                    var prop = reader.GetString();
                                    reader.Read();
                                    if (string.Equals(prop, "fileguid", StringComparison.OrdinalIgnoreCase))
                                        fg = reader.GetString();
                                    else if (string.Equals(prop, "extension", StringComparison.OrdinalIgnoreCase))
                                        ext = reader.GetString();
                                    else
                                        ; // skip
                                }
                            }

                            seq++;
                            var doc = new DocumentEntity
                            {
                                Id = Guid.NewGuid(),
                                JobId = jobId,
                                Filepath = fp,
                                FileGuid = fg ?? Guid.NewGuid().ToString(),
                                DocType = currentDoctype ?? string.Empty,
                                Country = country ?? string.Empty,
                                AppName = appname ?? string.Empty,
                                Status = DocumentStatus.Pending
                            };
                            docs.Add(doc);

                            if (docs.Count >= _opts.DbBatchSize)
                            {
                                await _repo.BulkInsertDocumentsAsync(docs, _opts.DbBatchSize, ct);
                                docs.Clear();
                            }
                        }
                    }
                }

                readerState = reader.CurrentState;
                var consumed = (int)reader.BytesConsumed;
                if (consumed < span.Length)
                {
                    var leftover = span.Length - consumed;
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, leftover);
                    bytesInBuffer = leftover;
                }
                else
                {
                    bytesInBuffer = 0;
                }

                if (read == 0) break;
            }

            if (docs.Count > 0)
            {
                await _repo.BulkInsertDocumentsAsync(docs, _opts.DbBatchSize, ct);
                docs.Clear();
            }

            _log.LogInformation("Staged metadata for job {JobId}", jobId);
        }
    }
}
