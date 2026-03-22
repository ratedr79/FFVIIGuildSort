using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class ZelarithAssignmentModel : PageModel
    {
        private readonly ILogger<ZelarithAssignmentModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly GuildBattleParser _parser = new();

        public ZelarithAssignmentModel(ILogger<ZelarithAssignmentModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        [BindProperty]
        public string? GoogleSheetUrl { get; set; }

        [BindProperty]
        public int CurrentDay { get; set; } = 1;

        [BindProperty]
        public double MarginOfErrorPercent { get; set; } = 0;

        public string? Output { get; set; }
        public string? ErrorMessage { get; set; }
        public bool HasRun { get; set; }
        public BattlePlanSummary? SimulationResult { get; set; }
        public DispatcherParsedPlan? ParsedPlan { get; set; }
        public List<SheetDefinition> AvailableSheets { get; set; } = new();

        public void OnGet()
        {
            LoadSheets();
        }

        public async Task<IActionResult> OnPostRunAsync()
        {
            LoadSheets();
            Stream? csvStream = null;

            try
            {
                // Get CSV data from Google Sheet or uploaded file
                if (!string.IsNullOrWhiteSpace(GoogleSheetUrl))
                {
                    using var httpClient = new HttpClient();
                    var response = await httpClient.GetAsync(GoogleSheetUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        ErrorMessage = $"Failed to download Google Sheet: {response.StatusCode}";
                        return Page();
                    }

                    csvStream = await response.Content.ReadAsStreamAsync();
                }
                else if (UploadedFile != null && UploadedFile.Length > 0)
                {
                    csvStream = UploadedFile.OpenReadStream();
                }
                else
                {
                    ErrorMessage = "Please select a Google Sheet or upload a CSV file.";
                    return Page();
                }

                // Read all CSV lines
                var allLines = new List<string>();
                using (var reader = new StreamReader(csvStream, Encoding.UTF8))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        allLines.Add(line);
                    }
                }

                // Extract B4:Q93 → rows 3..92 (0-indexed), columns 1..16 (0-indexed: B=1, Q=16)
                const int startRow = 3;   // row 4 (0-indexed)
                const int endRow = 92;    // row 93 (0-indexed)
                const int startCol = 1;   // column B (0-indexed)
                const int endCol = 16;    // column Q (0-indexed)

                var tsvLines = new List<string>();
                for (int r = startRow; r <= endRow && r < allLines.Count; r++)
                {
                    var fields = ParseCsvLine(allLines[r]);
                    var selectedFields = new List<string>();
                    for (int c = startCol; c <= endCol; c++)
                    {
                        selectedFields.Add(c < fields.Count ? fields[c] : "");
                    }
                    tsvLines.Add(string.Join("\t", selectedFields));
                }

                if (tsvLines.Count == 0)
                {
                    ErrorMessage = "No data found in the expected range (B4:Q93). Please check the CSV format.";
                    return Page();
                }

                // Write input.txt to Dispatcher directory
                var dispatcherPath = _configuration.GetValue<string>("Dispatcher:Path") ?? @"D:\code\SOLDIER\Dispatcher";
                var inputFilePath = Path.Combine(dispatcherPath, "input.txt");
                var exePath = Path.Combine(dispatcherPath, "Dispatcher.exe");

                if (!Directory.Exists(dispatcherPath))
                {
                    ErrorMessage = $"Dispatcher directory not found: {dispatcherPath}";
                    return Page();
                }

                if (!System.IO.File.Exists(exePath))
                {
                    ErrorMessage = $"Dispatcher.exe not found at: {exePath}";
                    return Page();
                }

                // Write the TSV data
                await System.IO.File.WriteAllTextAsync(inputFilePath, string.Join(Environment.NewLine, tsvLines));

                // Run Dispatcher.exe and capture output
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = dispatcherPath,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Send Enter key to stdin in case the exe waits for a keypress to exit
                await process.StandardInput.WriteLineAsync();
                process.StandardInput.Close();

                // Wait up to 30 seconds
                bool exited = process.WaitForExit(30_000);
                if (!exited)
                {
                    process.Kill();
                    ErrorMessage = "Dispatcher.exe timed out after 30 seconds.";
                    return Page();
                }

                HasRun = true;

                if (stderr.Length > 0)
                {
                    Output = stdout.ToString();
                    ErrorMessage = stderr.ToString();
                }
                else
                {
                    Output = stdout.ToString();
                }

                if (string.IsNullOrWhiteSpace(Output))
                {
                    Output = "(No output produced by Dispatcher.exe)";
                }
                else
                {
                    // Parse the Dispatcher output to extract stage assignments
                    ParsedPlan = ParseDispatcherOutput(Output);

                    if (ParsedPlan != null && ParsedPlan.StageAssignments.Any(s => s.Value.Count > 0))
                    {
                        // Re-read CSV to get player data for simulation
                        Stream? csvStream2 = null;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(GoogleSheetUrl))
                            {
                                using var httpClient2 = new HttpClient();
                                var response2 = await httpClient2.GetAsync(GoogleSheetUrl);
                                if (response2.IsSuccessStatusCode)
                                    csvStream2 = await response2.Content.ReadAsStreamAsync();
                            }
                            else if (UploadedFile != null && UploadedFile.Length > 0)
                            {
                                // Re-open the uploaded file stream
                                csvStream2 = UploadedFile.OpenReadStream();
                            }

                            if (csvStream2 != null)
                            {
                                var parseResult = await _parser.ParseAsync(csvStream2);
                                var players = parseResult?.Players;

                                if (players != null && players.Count > 0)
                                {
                                    CurrentDay = Math.Clamp(CurrentDay, 1, 3);
                                    var todayState = BuildTodayState(players, CurrentDay);

                                    var engine = new GuildBattleAssignmentEngine();
                                    SimulationResult = engine.SimulateWithFixedAssignments(
                                        ParsedPlan, players, todayState, MarginOfErrorPercent);
                                }
                            }
                        }
                        finally
                        {
                            csvStream2?.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running Dispatcher");
                ErrorMessage = $"Error: {ex.Message}";
            }
            finally
            {
                csvStream?.Dispose();
            }

            return Page();
        }

        private void LoadSheets()
        {
            var sheetsConfig = _configuration.GetSection("GoogleSheets:GuildBattleSheets")
                .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();
            AvailableSheets = sheetsConfig;
        }

        /// <summary>
        /// Build the current battle state by replaying all live attacks from the CSV.
        /// This properly handles mid-day imports where some players have already attacked.
        /// </summary>
        private static TodayState BuildTodayState(List<PlayerStageProfile> players, int currentDay)
        {
            var today = new TodayState
            {
                CurrentDay = currentDay,
                RemainingHpByStage = Enum.GetValues<StageId>().ToDictionary(s => s, s => 100.0),
                KillsToday = Enum.GetValues<StageId>().ToDictionary(s => s, s => 0)
            };

            int totalS5KillsAllDays = 0;

            // Count S5 kills across all days to determine S6 unlock
            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                foreach (var attempt in player.Attempts)
                {
                    if (attempt.Stage == StageId.S5 && attempt.Killed)
                        totalS5KillsAllDays++;
                }
            }

            today.IsStage6Unlocked = totalS5KillsAllDays >= 5;

            var unlockedStages = new HashSet<StageId>(Enum.GetValues<StageId>().Where(s => s != StageId.S6));
            if (today.IsStage6Unlocked) unlockedStages.Add(StageId.S6);

            foreach (var s in Enum.GetValues<StageId>())
                today.RemainingHpByStage[s] = unlockedStages.Contains(s) ? 100.0 : 0.0;

            // Replay all attempts in order to compute current HP state
            var allAttempts = new List<AttemptLog>();
            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                foreach (var attempt in player.Attempts)
                    allAttempts.Add(attempt);
            }
            allAttempts.Sort((a, b) =>
            {
                var dayCompare = a.Day.CompareTo(b.Day);
                if (dayCompare != 0) return dayCompare;
                return a.RowIndex.CompareTo(b.RowIndex);
            });

            // Reset S5 kill counter for progressive unlock tracking
            int s5KillsProgressive = 0;

            foreach (var a in allAttempts)
            {
                if (!unlockedStages.Contains(a.Stage)) continue;

                bool isToday = a.Day == currentDay;

                if (a.Killed)
                {
                    today.RemainingHpByStage[a.Stage] = 0.0;

                    if (isToday)
                        today.KillsToday[a.Stage] += 1;

                    if (a.Stage == StageId.S5)
                    {
                        s5KillsProgressive++;
                        if (!today.IsStage6Unlocked && s5KillsProgressive >= 5)
                        {
                            today.IsStage6Unlocked = true;
                            unlockedStages.Add(StageId.S6);
                            if (!today.RemainingHpByStage.ContainsKey(StageId.S6))
                                today.RemainingHpByStage[StageId.S6] = 100.0;
                        }
                    }
                }
                else
                {
                    var remaining = today.RemainingHpByStage[a.Stage];
                    var after = Math.Max(0.0, remaining - Math.Max(0.0, a.Percent));
                    today.RemainingHpByStage[a.Stage] = after;

                    if (after <= 0.0001)
                        today.RemainingHpByStage[a.Stage] = 0.0;
                }

                // Check for reset after each attack
                if (unlockedStages.All(s => today.RemainingHpByStage[s] <= 0.0001))
                {
                    foreach (var s in unlockedStages.ToArray())
                        today.RemainingHpByStage[s] = 100.0;
                }
            }

            // Count remaining hits
            int attemptsToday = 0;
            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                attemptsToday += player.Attempts.Count(a => a.Day == currentDay);
            }
            today.RemainingHits = Math.Max(0, 90 - attemptsToday);

            return today;
        }

        /// <summary>
        /// Parse the Dispatcher console output to extract stage assignments.
        /// Format: "--- Stage N ---" followed by "@Player" or "@Player (N)" entries.
        /// </summary>
        private static DispatcherParsedPlan ParseDispatcherOutput(string output)
        {
            var plan = new DispatcherParsedPlan { RawOutput = output };
            foreach (var stage in Enum.GetValues<StageId>())
                plan.StageAssignments[stage] = new List<DispatcherPlayerAssignment>();

            // Parse expected resets
            var resetMatch = Regex.Match(output, @"anticipating\s+(\d+)\s+reset", RegexOptions.IgnoreCase);
            if (resetMatch.Success && int.TryParse(resetMatch.Groups[1].Value, out int resets))
                plan.ExpectedResets = resets;

            // Parse stage sections
            var lines = output.Split('\n').Select(l => l.Trim()).ToArray();
            StageId? currentStage = null;

            foreach (var line in lines)
            {
                // Match "--- Stage N ---"
                var stageMatch = Regex.Match(line, @"---\s*Stage\s+(\d+)\s*---");
                if (stageMatch.Success && int.TryParse(stageMatch.Groups[1].Value, out int stageNum) && stageNum >= 1 && stageNum <= 6)
                {
                    currentStage = (StageId)stageNum;
                    continue;
                }

                // Match @Player entries on this stage
                if (currentStage.HasValue && line.Contains('@'))
                {
                    // Split by comma to get individual player entries
                    var entries = line.Split(',');
                    foreach (var entry in entries)
                    {
                        var trimmed = entry.Trim();
                        // Match @PlayerName or @PlayerName (N)
                        var playerMatch = Regex.Match(trimmed, @"@([\w\d]+)(?:\s*\((\d+)\))?");
                        if (playerMatch.Success)
                        {
                            var playerName = playerMatch.Groups[1].Value;
                            int attacks = 0; // 0 = all remaining
                            if (playerMatch.Groups[2].Success && int.TryParse(playerMatch.Groups[2].Value, out int count))
                                attacks = count;

                            plan.StageAssignments[currentStage.Value].Add(new DispatcherPlayerAssignment
                            {
                                PlayerName = playerName,
                                Attacks = attacks
                            });
                        }
                    }
                }
            }

            return plan;
        }

        /// <summary>
        /// Parse a CSV line handling quoted fields with commas inside them.
        /// </summary>
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++; // skip escaped quote
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
