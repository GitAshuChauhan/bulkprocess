using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Abstractions;

namespace Worker.Services
{
    public class CsvStager : ICsvStager
    {
        private readonly string _conn;
        private readonly ILogger<CsvStager> _logger;
        private static readonly string[] RequiredHeaders = new[]
        {
        "country","doctype","filepath","filename","filedescription",
        "fileguid","extension","operationtype","metadataonly","sensitivity","tag"
    };

        public CsvStager(IConfiguration cfg, ILogger<CsvStager> logger)
        {
            _conn = cfg.GetConnectionString("Postgres") ?? cfg["ConnectionStrings:Postgres"] ?? throw new InvalidOperationException("Postgres connection missing");
            _logger = logger;
        }

        // Streams CSV lines into DocumentStagingRaw via Postgres binary COPY.
        public async Task StageCsvAsync(Stream csvStream, Guid jobId, CancellationToken ct = default)
        {
            using var sr = new StreamReader(csvStream, leaveOpen: true);
            var header = await sr.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(header)) throw new InvalidOperationException("CSV empty or missing header");

            var headers = header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var missing = RequiredHeaders.Except(headers, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException($"Missing required headers: {string.Join(',', missing)}");

            await using var conn = new NpgsqlConnection(_conn);
            await conn.OpenAsync(ct);

            const string copyCmd = "COPY \"DocumentStagingRaw\" (\"Id\",\"JobId\",\"RawData\") FROM STDIN (FORMAT BINARY)";
            await using var importer = await conn.BeginBinaryImportAsync(copyCmd, ct);

            string? line;
            var rowCount = 0;
            while ((line = await sr.ReadLineAsync()) is not null)
            {
                ct.ThrowIfCancellationRequested();

                // write a new staging row: id, jobid, rawdata
                await importer.StartRowAsync(ct);
                await importer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                await importer.WriteAsync(jobId, NpgsqlDbType.Uuid, ct);
                await importer.WriteAsync(line, NpgsqlDbType.Text, ct);

                rowCount++;
                if ((rowCount & 0x3FF) == 0) // every 1024 rows, flush (binary import handles it)
                {
                    _logger.LogDebug("Staged {Count} rows so far for job {JobId}", rowCount, jobId);
                }
            }

            await importer.CompleteAsync(ct);
            _logger.LogInformation("CSV staging completed: staged {Count} rows for job {JobId}", rowCount, jobId);
        }
    }
}
