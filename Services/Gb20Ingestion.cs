using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class IngestionResult
    {
        public List<AccountRow> Accounts { get; set; } = new();
        public List<string> DuplicateSubmitters { get; set; } = new();
    }

    public sealed class Gb20Ingestion
    {
        public async Task<IngestionResult> ReadAccountsAsync(Stream csvStream)
        {
            using var reader = new StreamReader(csvStream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null,
            });

            var results = new List<AccountRow>();

            await csv.ReadAsync();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? Array.Empty<string>();

            var inGameNameHeader = headers.FirstOrDefault(h => h.Trim().Equals("In-Game Name", StringComparison.OrdinalIgnoreCase));
            if (inGameNameHeader == null)
            {
                throw new InvalidOperationException("gb20.csv must contain an 'In-Game Name' column.");
            }

            // Find timestamp column for deduplication
            var timestampHeader = headers.FirstOrDefault(h => h.Trim().Equals("Timestamp", StringComparison.OrdinalIgnoreCase));
            
            // Find Discord Name column
            var discordNameHeader = headers.FirstOrDefault(h => h.Trim().Equals("Discord Name (If different)", StringComparison.OrdinalIgnoreCase));

            while (await csv.ReadAsync())
            {
                var inGameName = (csv.GetField(inGameNameHeader) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(inGameName))
                {
                    continue;
                }

                var discordName = discordNameHeader != null 
                    ? (csv.GetField(discordNameHeader) ?? string.Empty).Trim() 
                    : string.Empty;

                var row = new AccountRow 
                { 
                    InGameName = inGameName,
                    DiscordName = string.IsNullOrWhiteSpace(discordName) ? null : discordName
                };

                foreach (var header in headers)
                {
                    var key = header?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var val = (csv.GetField(header) ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(val))
                    {
                        row.ItemResponsesByColumnName[key] = val;
                    }
                }

                results.Add(row);
            }

            // Deduplicate: keep only the latest row per in-game name based on timestamp
            var duplicateSubmitters = new List<string>();
            
            if (timestampHeader != null)
            {
                // Identify players who submitted more than once
                duplicateSubmitters = results
                    .GroupBy(r => r.InGameName, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                results = results
                    .GroupBy(r => r.InGameName, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        // Parse timestamps and select the row with the latest timestamp
                        var withTimestamps = g.Select(row =>
                        {
                            DateTime? timestamp = null;
                            if (row.ItemResponsesByColumnName.TryGetValue(timestampHeader, out var tsValue))
                            {
                                if (DateTime.TryParse(tsValue, out var parsed))
                                {
                                    timestamp = parsed;
                                }
                            }
                            return new { Row = row, Timestamp = timestamp };
                        }).ToList();

                        // Select the row with the latest timestamp, or first if no valid timestamps
                        var latest = withTimestamps
                            .OrderByDescending(x => x.Timestamp ?? DateTime.MinValue)
                            .First();

                        return latest.Row;
                    })
                    .ToList();
            }

            return new IngestionResult
            {
                Accounts = results,
                DuplicateSubmitters = duplicateSubmitters
            };
        }
    }
}
