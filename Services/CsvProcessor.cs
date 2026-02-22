using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public class CsvProcessor
    {
        public CsvProcessor()
        {
        }

        public async Task<List<PlayerData>> ProcessCsvAsync(Stream csvStream)
        {
            var players = new List<PlayerData>();

            using var reader = new StreamReader(csvStream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null
            });

            try
            {
                var records = csv.GetRecords<PlayerData>().ToList();
                
                foreach (var player in records)
                {
                    players.Add(player);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error processing CSV file: {ex.Message}", ex);
            }

            return players;
        }

        public async Task<byte[]> ExportResultsAsync(List<PlayerData> players)
        {
            using var output = new MemoryStream();
            using var writer = new StreamWriter(output);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

            await csv.WriteRecordsAsync(players);
            await writer.FlushAsync();
            
            return output.ToArray();
        }

        public string GenerateSampleCsv()
        {
            var sampleData = new List<PlayerData>
            {
                new PlayerData 
                { 
                    PlayerName = "Cloud_Strife", 
                    CharacterName = "Cloud", 
                    Level = 80, 
                    HP = 15000, 
                    Attack = 2500, 
                    Defense = 1800, 
                    Magic = 1200, 
                    MagicDefense = 1500, 
                    Speed = 200, 
                    CriticalRate = 85, 
                    Evasion = 60, 
                    WeaponLevel = 15, 
                    ArmorLevel = 12, 
                    AccessoryLevel = 10, 
                    AbilityLevel = 8, 
                    LimitBreakLevel = 5, 
                    OverlordLevel = 3 
                },
                new PlayerData 
                { 
                    PlayerName = "Sephiroth_Pro", 
                    CharacterName = "Sephiroth", 
                    Level = 85, 
                    HP = 16000, 
                    Attack = 2800, 
                    Defense = 2000, 
                    Magic = 2200, 
                    MagicDefense = 1800, 
                    Speed = 220, 
                    CriticalRate = 90, 
                    Evasion = 70, 
                    WeaponLevel = 18, 
                    ArmorLevel = 14, 
                    AccessoryLevel = 12, 
                    AbilityLevel = 10, 
                    LimitBreakLevel = 6, 
                    OverlordLevel = 4 
                },
                new PlayerData 
                { 
                    PlayerName = "Tifa_Lockhart", 
                    CharacterName = "Tifa", 
                    Level = 78, 
                    HP = 14000, 
                    Attack = 2300, 
                    Defense = 1600, 
                    Magic = 1000, 
                    MagicDefense = 1400, 
                    Speed = 210, 
                    CriticalRate = 88, 
                    Evasion = 65, 
                    WeaponLevel = 14, 
                    ArmorLevel = 11, 
                    AccessoryLevel = 9, 
                    AbilityLevel = 7, 
                    LimitBreakLevel = 5, 
                    OverlordLevel = 2 
                }
            };

            using var output = new StringWriter();
            using var csv = new CsvWriter(output, new CsvConfiguration(CultureInfo.InvariantCulture));
            
            csv.WriteRecords(sampleData);
            return output.ToString();
        }
    }
}
