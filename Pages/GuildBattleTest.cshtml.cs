using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FFVIIEverCrisisAnalyzer.Services;
using FFVIIEverCrisisAnalyzer.Models;
using System.Text;
using System.Text.Json;
using System;
using System.Linq;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class GuildBattleTestModel : PageModel
    {
        private readonly ILogger<GuildBattleTestModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly GuildBattleParser _parser = new();
        private readonly SimulationHarness _harness = new();

        public GuildBattleTestModel(ILogger<GuildBattleTestModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // Form inputs
        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        [BindProperty]
        public string? GoogleSheetUrl { get; set; }

        [BindProperty]
        public int CurrentDay { get; set; } = 1;

        [BindProperty]
        public double MarginOfErrorPercent { get; set; } = 1.0;

        [BindProperty]
        public int NumberOfRuns { get; set; } = 10;

        [BindProperty]
        public string SeedMode { get; set; } = "Auto";

        [BindProperty]
        public int FixedSeed { get; set; } = 42;

        [BindProperty]
        public bool EnableVariance { get; set; } = true;

        [BindProperty]
        public bool EnableOutlierFilter { get; set; } = true;

        [BindProperty]
        public bool EnableDeviationCap { get; set; } = true;

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

        [BindProperty]
        public string ActiveTab { get; set; } = "multi";

        [BindProperty]
        public string ScoreBy { get; set; } = "MinSumFinalHP";

        // Results
        public AggregatedTestResults? Results { get; set; }
        public string? ErrorMessage { get; set; }
        public List<SheetDefinition> AvailableSheets { get; set; } = new();

        public void OnGet()
        {
            LoadSheets();
        }

        public async Task<IActionResult> OnPostMultiRunAsync()
        {
            ActiveTab = "multi";
            return await RunSimulation(false);
        }

        public async Task<IActionResult> OnPostSingleRunAsync()
        {
            ActiveTab = "single";
            return await RunSimulation(true);
        }

        public async Task<IActionResult> OnPostExportCsvAsync()
        {
            // Re-run and export
            ActiveTab = "multi";
            var result = await RunSimulationInternal(false);
            if (result == null) return Page();

            var sb = new StringBuilder();
            sb.AppendLine("Metric,Value");
            sb.AppendLine($"Total Runs,{result.TotalRuns}");
            sb.AppendLine($"Avg Resets,{result.AvgResets:F2}");
            sb.AppendLine($"Min Resets,{result.MinResets}");
            sb.AppendLine($"Max Resets,{result.MaxResets}");
            sb.AppendLine($"P10 Resets,{result.P10Resets:F1}");
            sb.AppendLine($"P90 Resets,{result.P90Resets:F1}");
            sb.AppendLine($"S6 Unlock Rate,{result.Stage6UnlockRatePercent:F1}%");
            sb.AppendLine();

            sb.AppendLine("Stage,Avg Final HP,Avg Clears,Clear Rate %");
            foreach (var stage in Enum.GetValues<StageId>())
            {
                sb.AppendLine($"S{(int)stage},{result.AvgFinalHP.GetValueOrDefault(stage):F2},{result.AvgClears.GetValueOrDefault(stage):F2},{result.ClearRatePercent.GetValueOrDefault(stage):F1}");
            }
            sb.AppendLine();

            sb.AppendLine("Player,S1,S2,S3,S4,S5,S6");
            foreach (var kvp in result.AssignmentFrequency.OrderBy(x => x.Key))
            {
                sb.Append(kvp.Key);
                foreach (var stage in Enum.GetValues<StageId>())
                {
                    sb.Append($",{kvp.Value.GetValueOrDefault(stage)}");
                }
                sb.AppendLine();
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"sim-test-results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        public async Task<IActionResult> OnPostExportJsonAsync()
        {
            ActiveTab = "multi";
            var result = await RunSimulationInternal(false);
            if (result == null) return Page();

            // Exclude heavy attack logs from JSON export
            var exportData = new
            {
                result.TotalRuns,
                result.Settings,
                result.AvgResets,
                result.MinResets,
                result.MaxResets,
                result.P10Resets,
                result.P90Resets,
                result.AvgFinalHP,
                result.AvgClears,
                result.ClearRatePercent,
                result.Stage6UnlockRatePercent,
                result.AssignmentFrequency,
                result.TotalSimulatedAttacks,
                RunSummaries = result.Runs.Select(r => new
                {
                    r.RunIndex,
                    r.Seed,
                    r.Resets,
                    r.FinalHP,
                    r.StageClears,
                    r.TotalAttacks
                })
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            var fileName = $"sim-test-results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            return File(bytes, "application/json", fileName);
        }

        private async Task<IActionResult> RunSimulation(bool singleDetailed)
        {
            var result = await RunSimulationInternal(singleDetailed);
            if (result != null) Results = result;
            return Page();
        }

        private async Task<AggregatedTestResults?> RunSimulationInternal(bool singleDetailed)
        {
            LoadSheets();
            Stream? csvStream = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(GoogleSheetUrl))
                {
                    using var httpClient = new HttpClient();
                    var response = await httpClient.GetAsync(GoogleSheetUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        ErrorMessage = $"Failed to download Google Sheet: {response.StatusCode}";
                        return null;
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
                    return null;
                }

                var parseResult = await _parser.ParseAsync(csvStream);
                var players = parseResult?.Players;
                if (players == null || players.Count == 0)
                {
                    ErrorMessage = "No player data found in CSV.";
                    return null;
                }

                CurrentDay = Math.Clamp(CurrentDay, 1, 3);
                NumberOfRuns = Math.Clamp(NumberOfRuns, 1, 50);

                var today = TodayStateBuilder.BuildTodayState(
                    players,
                    CurrentDay,
                    TodayStateBuildMode.OrderAgnosticAggregate,
                    out _);

                // Apply HP overrides if enabled
                if (HpOverrideEnabled)
                {
                    ApplyHpOverrides(today);
                }

                var settings = new SimulationTestSettings
                {
                    NumberOfRuns = NumberOfRuns,
                    CurrentDay = CurrentDay,
                    MarginOfErrorPercent = MarginOfErrorPercent,
                    SeedMode = SeedMode,
                    FixedSeed = FixedSeed,
                    EnableVariance = EnableVariance,
                    EnableOutlierFilter = EnableOutlierFilter,
                    EnableDeviationCap = EnableDeviationCap,
                    OvershootTriggerPercent = OvershootTriggerPercent,
                    CleanupConfidenceBufferPercent = CleanupConfidenceBufferPercent
                };

                return singleDetailed
                    ? _harness.RunSingleDetailed(players, today, settings)
                    : _harness.RunMultiple(players, today, settings, ScoreBy);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error: {ex.Message}";
                _logger.LogError(ex, "Error running simulation test");
                return null;
            }
            finally
            {
                if (csvStream != null && !string.IsNullOrWhiteSpace(GoogleSheetUrl))
                {
                    await csvStream.DisposeAsync();
                }
            }
        }

        private void LoadSheets()
        {
            var sheetsConfig = _configuration.GetSection("GoogleSheets:GuildBattleSheets")
                .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();
            AvailableSheets = sheetsConfig;
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
