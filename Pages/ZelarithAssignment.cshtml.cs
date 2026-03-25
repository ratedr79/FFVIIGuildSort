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
        private readonly StagePointEstimator _pointEstimator;
        private readonly GuildBattleParser _parser = new();

        public ZelarithAssignmentModel(ILogger<ZelarithAssignmentModel> logger, IConfiguration configuration, StagePointEstimator pointEstimator)
        {
            _logger = logger;
            _configuration = configuration;
            _pointEstimator = pointEstimator;
        }

        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        [BindProperty]
        public string? GoogleSheetUrl { get; set; }

        [BindProperty]
        public int CurrentDay { get; set; } = 1;

        [BindProperty]
        public double MarginOfErrorPercent { get; set; } = 0;

        [BindProperty]
        public int NumberOfRuns { get; set; } = 5;

        [BindProperty]
        public double OvershootTriggerPercent { get; set; } = 20;

        [BindProperty]
        public double CleanupConfidenceBufferPercent { get; set; } = 15;

        [BindProperty]
        public bool HpOverrideEnabled { get; set; }

        [BindProperty]
        public double? HpS1 { get; set; }

        [BindProperty]
        public double? HpS2 { get; set; }

        [BindProperty]
        public double? HpS3 { get; set; }

        [BindProperty]
        public double? HpS4 { get; set; }

        [BindProperty]
        public double? HpS5 { get; set; }

        [BindProperty]
        public double? HpS6 { get; set; }

        [BindProperty]
        public bool HpS6Unlocked { get; set; }

        public string? Output { get; set; }
        public string? ErrorMessage { get; set; }
        public bool HasRun { get; set; }
        public BattlePlanSummary? SimulationResult { get; set; }
        public AggregatedTestResults? AggregatedResults { get; set; }
        public DispatcherParsedPlan? ParsedPlan { get; set; }
        public StageHpComputationDebug? HpComputationDebug { get; set; }
        public List<PlayerStageProfile> Players { get; set; } = new();
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
                using (var csvReader = new StreamReader(csvStream, Encoding.UTF8))
                {
                    string? line;
                    while ((line = await csvReader.ReadLineAsync()) != null)
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

                process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                process.Start();
                process.BeginErrorReadLine();

                // Read stdout until the dispatcher blocks waiting for input.
                // Interactive prompts typically don't end with a newline, so we
                // read char-by-char with a timeout to detect when it's waiting.
                var reader = process.StandardOutput;
                var buf = new char[1];
                bool inputSent = false;
                using var cts = new CancellationTokenSource(30_000);

                try
                {
                    Task<int>? pendingRead = null;
                    while (!process.HasExited)
                    {
                        // Reuse any still-pending read from the previous iteration
                        var readTask = pendingRead ?? reader.ReadAsync(buf, 0, 1);
                        pendingRead = null;
                        var completed = await Task.WhenAny(readTask, Task.Delay(1500, cts.Token));

                        if (completed == readTask)
                        {
                            int charsRead = await readTask;
                            if (charsRead == 0) break; // EOF
                            stdout.Append(buf[0]);
                        }
                        else if (!inputSent)
                        {
                            // No output for 1.5s — the dispatcher is likely waiting for input.
                            // Keep the pending read alive for reuse after we send input.
                            pendingRead = readTask;

                            // Parse the first line to extract the day the dispatcher detected,
                            // e.g. "Based on data, it seems to be day 3 of the official matches."
                            string outputSoFar = stdout.ToString();
                            _logger.LogInformation("Dispatcher prompt detected: {Prompt}", outputSoFar);

                            int detectedDay = 0;
                            var dayMatch = System.Text.RegularExpressions.Regex.Match(outputSoFar, @"day\s+(\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (dayMatch.Success)
                                detectedDay = int.Parse(dayMatch.Groups[1].Value);

                            if (detectedDay != CurrentDay && CurrentDay >= 1 && CurrentDay <= 3)
                            {
                                _logger.LogInformation("Dispatcher detected day {Detected}, overriding to day {Selected}", detectedDay, CurrentDay);
                                await process.StandardInput.WriteLineAsync(CurrentDay.ToString());
                            }
                            else
                            {
                                // Day matches — accept default
                                await process.StandardInput.WriteLineAsync();
                            }
                            process.StandardInput.Close();
                            inputSent = true;
                        }
                        else
                        {
                            // Input already sent but no output — keep pending read and break
                            pendingRead = readTask;
                            break;
                        }
                    }

                    // Read any remaining output after process exits
                    var remaining = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(remaining))
                        stdout.Append(remaining);
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    ErrorMessage = "Dispatcher.exe timed out after 30 seconds.";
                    return Page();
                }

                if (!inputSent)
                {
                    // Process exited without ever waiting for input — close stdin
                    try { process.StandardInput.Close(); } catch { }
                }

                process.WaitForExit(5_000); // final cleanup wait
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
                                    Players = players;
                                    CurrentDay = Math.Clamp(CurrentDay, 1, 3);
                                    NumberOfRuns = Math.Clamp(NumberOfRuns, 1, 50);
                                    OvershootTriggerPercent = Math.Clamp(OvershootTriggerPercent, 0, 100);
                                    CleanupConfidenceBufferPercent = Math.Clamp(CleanupConfidenceBufferPercent, 0, 100);
                                    var todayState = TodayStateBuilder.BuildTodayState(
                                        players,
                                        CurrentDay,
                                        TodayStateBuildMode.OrderAgnosticAggregate,
                                        out var hpComputationDebug);
                                    HpComputationDebug = hpComputationDebug;

                                    // Apply HP overrides if enabled
                                    if (HpOverrideEnabled)
                                    {
                                        ApplyHpOverrides(todayState);
                                    }

                                    var rng = new Random();
                                    var aggregated = new AggregatedTestResults
                                    {
                                        TotalRuns = NumberOfRuns,
                                        Settings = new SimulationTestSettings
                                        {
                                            NumberOfRuns = NumberOfRuns,
                                            CurrentDay = CurrentDay,
                                            MarginOfErrorPercent = MarginOfErrorPercent
                                        }
                                    };

                                    for (int i = 0; i < NumberOfRuns; i++)
                                    {
                                        int seed = rng.Next();
                                        var engine = new GuildBattleAssignmentEngine(seed, _pointEstimator);
                                        var plan = engine.SimulateWithFixedAssignments(
                                            ParsedPlan,
                                            players,
                                            todayState,
                                            MarginOfErrorPercent,
                                            OvershootTriggerPercent,
                                            CleanupConfidenceBufferPercent);

                                        int attacksMade = plan.AttackLog.Count(a => !a.IsReset);
                                        var run = new SingleRunResult
                                        {
                                            RunIndex = i + 1,
                                            Seed = seed,
                                            Resets = plan.ExpectedResets,
                                            FinalHP = new Dictionary<StageId, double>(plan.FinalHpByStage),
                                            StageClears = new Dictionary<StageId, int>(plan.StageClears),
                                            Stage6Unlocked = todayState.IsStage6Unlocked,
                                            AttackLog = plan.AttackLog,
                                            TotalAttacks = attacksMade,
                                            AttemptsAvailable = todayState.RemainingHits,
                                            AttemptsUsed = attacksMade
                                        };
                                        foreach (var group in plan.StageGroups)
                                            run.Assignments[group.Stage] = new List<string>(group.PlayerNames);

                                        aggregated.Runs.Add(run);
                                    }

                                    AggregateDispatcherResults(aggregated);
                                    AggregatedResults = aggregated;

                                    // Use the best run as the primary SimulationResult
                                    var bestRun = aggregated.Runs.FirstOrDefault(r => r.RunIndex == aggregated.BestRunIndex)
                                        ?? aggregated.Runs.First();
                                    SimulationResult = new BattlePlanSummary
                                    {
                                        ExpectedResets = bestRun.Resets,
                                        FinalHpByStage = bestRun.FinalHP,
                                        StageClears = bestRun.StageClears,
                                        AttackLog = bestRun.AttackLog
                                    };
                                    foreach (var kvp in bestRun.Assignments)
                                        SimulationResult.StageGroups.Add(new StageAssignmentGroup { Stage = kvp.Key, PlayerNames = kvp.Value });
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
        /// Aggregate statistics across multiple dispatcher simulation runs.
        /// </summary>
        private static void AggregateDispatcherResults(AggregatedTestResults results)
        {
            if (results.Runs.Count == 0) return;

            var resets = results.Runs.Select(r => (double)r.Resets).OrderBy(x => x).ToList();
            results.AvgResets = resets.Average();
            results.MinResets = resets.Min();
            results.MaxResets = resets.Max();
            results.P10Resets = Percentile(resets, 10);
            results.P90Resets = Percentile(resets, 90);

            foreach (var stage in Enum.GetValues<StageId>())
            {
                var finalHps = results.Runs.Select(r => r.FinalHP.GetValueOrDefault(stage, 0)).ToList();
                results.AvgFinalHP[stage] = finalHps.Average();

                var clears = results.Runs.Select(r => (double)r.StageClears.GetValueOrDefault(stage, 0)).ToList();
                results.AvgClears[stage] = clears.Average();

                var clearedRuns = results.Runs.Count(r => r.StageClears.GetValueOrDefault(stage, 0) > 0);
                results.ClearRatePercent[stage] = (double)clearedRuns / results.Runs.Count * 100.0;
            }

            var s6Unlocked = results.Runs.Count(r => r.Stage6Unlocked);
            results.Stage6UnlockRatePercent = (double)s6Unlocked / results.Runs.Count * 100.0;

            results.TotalSimulatedAttacks = results.Runs.Sum(r => r.TotalAttacks);

            // Score runs: more resets = better, lower sum of final HP = better
            if (results.Runs.Count >= 2)
            {
                var scored = results.Runs
                    .Select(r => new { r.RunIndex, Score = (-r.Resets, r.FinalHP.Values.Sum()) })
                    .OrderBy(x => x.Score)
                    .ToList();
                results.BestRunIndex = scored.First().RunIndex;
                results.WorstRunIndex = scored.Last().RunIndex;
            }
            else
            {
                results.BestRunIndex = results.Runs[0].RunIndex;
                results.WorstRunIndex = results.Runs[0].RunIndex;
            }
        }

        private static double Percentile(List<double> sorted, int percentile)
        {
            if (sorted.Count == 0) return 0;
            if (sorted.Count == 1) return sorted[0];
            double index = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = Math.Min(lower + 1, sorted.Count - 1);
            double weight = index - lower;
            return sorted[lower] * (1 - weight) + sorted[upper] * weight;
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

        private void ApplyHpOverrides(TodayState today)
        {
            var overrides = new (StageId stage, double? value)[]
            {
                (StageId.S1, HpS1), (StageId.S2, HpS2), (StageId.S3, HpS3),
                (StageId.S4, HpS4), (StageId.S5, HpS5), (StageId.S6, HpS6)
            };
            foreach (var (stage, value) in overrides)
            {
                if (value.HasValue)
                    today.RemainingHpByStage[stage] = Math.Clamp(value.Value, 0, 100);
            }
            today.IsStage6Unlocked = HpS6Unlocked;
        }
    }
}
