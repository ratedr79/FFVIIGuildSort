using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class Gb20Ingestion
    {
        public async Task<List<AccountRow>> ReadAccountsAsync(Stream csvStream)
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

            while (await csv.ReadAsync())
            {
                var inGameName = (csv.GetField(inGameNameHeader) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(inGameName))
                {
                    continue;
                }

                var row = new AccountRow { InGameName = inGameName };

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

            return results;
        }
    }
}
