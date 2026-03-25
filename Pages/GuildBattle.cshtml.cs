using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FFVIIEverCrisisAnalyzer.Services;
using FFVIIEverCrisisAnalyzer.Models;
using System.Text;
using System;
using System.Linq;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class GuildBattleModel : PageModel
    {
        private readonly ILogger<GuildBattleModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly StagePointEstimator _pointEstimator;
        private readonly GuildBattleParser _parser = new();
        private GuildBattleAssignmentEngine _engine;

        public GuildBattleModel(ILogger<GuildBattleModel> logger, IConfiguration configuration, StagePointEstimator pointEstimator)
        {
            _logger = logger;
            _configuration = configuration;
            _pointEstimator = pointEstimator;
            _engine = new GuildBattleAssignmentEngine(_pointEstimator);
        }

        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        [BindProperty]
        public string? GoogleSheetUrl { get; set; }

        [BindProperty]
        public int CurrentDay { get; set; } = 1; // 1..3

        [BindProperty]
        public double MarginOfErrorPercent { get; set; } = 1.0; // default

        [BindProperty]
        public double OvershootTriggerPercent { get; set; } = 20;

        [BindProperty]
        public double CleanupConfidenceBufferPercent { get; set; } = 15;

        [BindProperty]
        public int? Seed { get; set; }

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

        public GuildBattleParseResult? ParseResult { get; set; }
        public TodayState? Today { get; set; }
        public BattlePlanSummary? BattlePlan { get; set; }
        public string? ErrorMessage { get; set; }
        public List<SheetDefinition> AvailableSheets { get; set; } = new();

        public void OnGet(int? seed, int? day, double? margin, double? overshoot, double? cleanup,
            bool? hpOverride, double? hpS1, double? hpS2, double? hpS3, double? hpS4, double? hpS5, double? hpS6, bool? hpS6Unlocked)
        {
            // Read query params from Test page seed links
            if (seed.HasValue) Seed = seed.Value;
            if (day.HasValue) CurrentDay = Math.Clamp(day.Value, 1, 3);
            if (margin.HasValue) MarginOfErrorPercent = margin.Value;
            if (overshoot.HasValue) OvershootTriggerPercent = overshoot.Value;
            if (cleanup.HasValue) CleanupConfidenceBufferPercent = cleanup.Value;
            if (hpOverride == true) HpOverrideEnabled = true;
            if (hpS1.HasValue) HpS1 = hpS1.Value;
            if (hpS2.HasValue) HpS2 = hpS2.Value;
            if (hpS3.HasValue) HpS3 = hpS3.Value;
            if (hpS4.HasValue) HpS4 = hpS4.Value;
            if (hpS5.HasValue) HpS5 = hpS5.Value;
            if (hpS6.HasValue) HpS6 = hpS6.Value;
            if (hpS6Unlocked == true) HpS6Unlocked = true;

            // Load Google Sheets configuration from appsettings.json
            var sheetsConfig = _configuration.GetSection("GoogleSheets:GuildBattleSheets")
                .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();
            AvailableSheets = sheetsConfig;
        }

        public async Task<IActionResult> OnPostAnalyzeAsync()
        {
            Stream? csvStream = null;
            
            try
            {
                // Check if Google Sheets URL is provided
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
                // Otherwise check for uploaded file
                else if (UploadedFile != null && UploadedFile.Length > 0)
                {
                    csvStream = UploadedFile.OpenReadStream();
                }
                else
                {
                    ErrorMessage = "Please select a Google Sheet or upload a CSV file.";
                    return Page();
                }

                ParseResult = await _parser.ParseAsync(csvStream);

                var players = ParseResult?.Players;
                if (players == null || players.Count == 0)
                {
                    ErrorMessage = "No player data found in CSV.";
                    return Page();
                }

                CurrentDay = Math.Clamp(CurrentDay, 1, 3);
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

                Today = today;

                // Use seeded engine if seed is provided
                if (Seed.HasValue)
                {
                    _engine = new GuildBattleAssignmentEngine(Seed.Value, _pointEstimator);
                }

                // Generate battle plan using the engine
                BattlePlan = _engine.GenerateBattlePlan(players, today, MarginOfErrorPercent, OvershootTriggerPercent, CleanupConfidenceBufferPercent);
                
                // Repopulate AvailableSheets for the result page
                var sheetsConfig = _configuration.GetSection("GoogleSheets:GuildBattleSheets")
                    .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();
                AvailableSheets = sheetsConfig;
                
                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error parsing file: {ex.Message}";
                _logger.LogError(ex, "Error parsing Guild Battle CSV");
                
                // Repopulate AvailableSheets even on error
                var sheetsConfig = _configuration.GetSection("GoogleSheets:GuildBattleSheets")
                    .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();
                AvailableSheets = sheetsConfig;
                
                return Page();
            }
            finally
            {
                // Dispose of the stream if it was downloaded from Google Sheets
                if (csvStream != null && !string.IsNullOrWhiteSpace(GoogleSheetUrl))
                {
                    await csvStream.DisposeAsync();
                }
            }
        }

        public IActionResult OnPostExportAsync()
        {
            if (BattlePlan == null || BattlePlan.StageGroups.Count == 0)
            {
                ErrorMessage = "No battle plan to export. Analyze first.";
                return Page();
            }

            var sb = new StringBuilder();
            sb.AppendLine("Stage,Player");
            foreach (var group in BattlePlan.StageGroups)
            {
                foreach (var player in group.PlayerNames)
                {
                    sb.Append((int)group.Stage).Append(',');
                    sb.AppendLine(player);
                }
            }

            var utf8WithBom = new UTF8Encoding(true);
            using var memoryStream = new MemoryStream();
            using (var writer = new StreamWriter(memoryStream, utf8WithBom, leaveOpen: true))
            {
                writer.Write(sb.ToString());
            }
            var bytes = memoryStream.ToArray();
            var fileName = $"guild-battle-plan_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
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
