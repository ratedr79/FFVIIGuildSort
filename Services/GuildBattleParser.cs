using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FFVIIEverCrisisAnalyzer.Models;
using System.Linq;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class GuildBattleParseResult
    {
        public List<PlayerStageProfile> Players { get; set; } = new();
    }

    public sealed class GuildBattleParser
    {
        private static double ParsePercent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            // Normalize comma decimal separators (e.g. "12,5" → "12.5") before stripping
            var normalized = text.Replace(',', '.');
            var cleaned = new string(normalized.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
            return 0;
        }

        private static bool ParseBool(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                   || text.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
                   || text.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
                   || text.Trim().Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private static StageId ToStageId(int stage)
        {
            return stage switch
            {
                1 => StageId.S1,
                2 => StageId.S2,
                3 => StageId.S3,
                4 => StageId.S4,
                5 => StageId.S5,
                6 => StageId.S6,
                _ => StageId.S1
            };
        }

        public async Task<GuildBattleParseResult> ParseAsync(Stream csvStream)
        {
            using var reader = new StreamReader(csvStream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim
            });

            var rows = new List<string[]>();
            const int MAX_ROWS = 1000; // Safety limit
            int rowCount = 0;
            
            while (await csv.ReadAsync())
            {
                if (rowCount >= MAX_ROWS)
                {
                    throw new InvalidOperationException($"CSV file exceeds maximum row limit of {MAX_ROWS}. Please check the file format.");
                }
                
                var rec = new List<string>();
                for (int i = 0; i < 50 && csv.TryGetField(i, out string? val); i++) // Limit columns too
                {
                    rec.Add(val ?? string.Empty);
                }
                rows.Add(rec.ToArray());
                rowCount++;
            }

            var result = new GuildBattleParseResult();

            if (rows.Count < 4)
            {
                return result;
            }

            // Header rows: 0,1,2. Stage numbers in row 2, columns C-H (2..7)
            var stageMap = new Dictionary<int, StageId>();
            for (int col = 2; col <= 7; col++)
            {
                if (col < rows[2].Length)
                {
                    var cell = rows[2][col];
                    if (int.TryParse(new string(cell.Where(char.IsDigit).ToArray()), out int stageNo) && stageNo >= 1 && stageNo <= 6)
                    {
                        stageMap[col] = ToStageId(stageNo);
                    }
                }
            }

            // Iterate players in groups of 3 rows: starting from index 3
            for (int r = 3; r + 2 < rows.Count; r += 3)
            {
                var row0 = rows[r];
                var row1 = rows[r + 1];
                var row2 = rows[r + 2];
                var name = (row0.Length > 1 ? row0[1] : string.Empty)?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var profile = new PlayerStageProfile { Name = name };

                // Mock percents from columns C-H on the first row of the trio
                foreach (var kvp in stageMap)
                {
                    var col = kvp.Key;
                    if (col < row0.Length)
                    {
                        var pct = ParsePercent(row0[col]);
                        profile.MockPercents[kvp.Value] = Math.Max(0, pct);
                    }
                }

                // Attempts per day: Day1 (I-K 8..10), Day2 (L-N 11..13), Day3 (O-Q 14..16)
                void AddAttemptRow(string[] sourceRow, int baseCol, int day, int rowIndex)
                {
                    var stageText = baseCol < sourceRow.Length ? sourceRow[baseCol] : string.Empty;
                    var percentText = baseCol + 1 < sourceRow.Length ? sourceRow[baseCol + 1] : string.Empty;
                    var killText = baseCol + 2 < sourceRow.Length ? sourceRow[baseCol + 2] : string.Empty;
                    if (!string.IsNullOrWhiteSpace(stageText))
                    {
                        if (int.TryParse(new string(stageText.Where(char.IsDigit).ToArray()), out int stageNo) && stageNo >= 1 && stageNo <= 6)
                        {
                            var log = new AttemptLog
                            {
                                PlayerName = name,
                                Day = day,
                                Stage = ToStageId(stageNo),
                                Percent = ParsePercent(percentText),
                                Killed = ParseBool(killText),
                                RowIndex = rowIndex
                            };
                            profile.Attempts.Add(log);
                        }
                    }
                }

                // row order preserved by RowIndex
                AddAttemptRow(row0, 8, 1, r);
                AddAttemptRow(row1, 8, 1, r + 1);
                AddAttemptRow(row2, 8, 1, r + 2);

                AddAttemptRow(row0, 11, 2, r);
                AddAttemptRow(row1, 11, 2, r + 1);
                AddAttemptRow(row2, 11, 2, r + 2);

                AddAttemptRow(row0, 14, 3, r);
                AddAttemptRow(row1, 14, 3, r + 1);
                AddAttemptRow(row2, 14, 3, r + 2);

                // Notes (R index 17), aggregate
                var notes = new List<string>();
                if (row0.Length > 17 && !string.IsNullOrWhiteSpace(row0[17])) notes.Add(row0[17].Trim());
                if (row1.Length > 17 && !string.IsNullOrWhiteSpace(row1[17])) notes.Add(row1[17].Trim());
                if (row2.Length > 17 && !string.IsNullOrWhiteSpace(row2[17])) notes.Add(row2[17].Trim());
                profile.Notes = string.Join(" | ", notes);

                result.Players.Add(profile);
            }

            // Calculate averaged percentages for all players
            CalculateAveragedPercentages(result.Players);

            return result;
        }

        /// <summary>
        /// Calculate running averages for each player/stage combination using hybrid approach:
        /// 1. Filter cleanup kills (Killed=true AND damage < 3%) - likely finishing low HP bosses
        /// 2. Use weighted average: Mock 40%, Live attacks 60%
        /// 3. Cap downward deviation: Don't drop more than 30% below mock
        /// </summary>
        private void CalculateAveragedPercentages(List<PlayerStageProfile> players)
        {
            foreach (var player in players)
            {
                foreach (var stage in Enum.GetValues<StageId>())
                {
                    // Start with mock percentage
                    var mockPercent = player.MockPercents.GetValueOrDefault(stage, 0);
                    
                    // Get all live attacks for this stage, filtering outliers
                    var liveAttacks = player.Attempts
                        .Where(a => a.Stage == stage && a.Percent > 0.001) // Ignore 0% attacks
                        .Where(a => !(a.Killed && a.Percent < 3.0)) // Filter cleanup kills (killed with < 3% damage)
                        .Select(a => a.Percent)
                        .ToList();
                    
                    double averagedPercent;
                    
                    if (liveAttacks.Any())
                    {
                        // Weighted average: Mock 40%, Live attacks 60%
                        var liveAverage = liveAttacks.Average();
                        averagedPercent = (mockPercent * 0.4) + (liveAverage * 0.6);
                        
                        // Cap downward deviation: Don't drop more than 30% below mock
                        var minimumAllowed = mockPercent * 0.7; // 70% of mock
                        if (averagedPercent < minimumAllowed)
                        {
                            averagedPercent = minimumAllowed;
                        }
                    }
                    else
                    {
                        // No valid live data, use mock percentage
                        averagedPercent = mockPercent;
                    }
                    
                    player.AveragedPercents[stage] = averagedPercent;
                }
            }
        }
    }
}
