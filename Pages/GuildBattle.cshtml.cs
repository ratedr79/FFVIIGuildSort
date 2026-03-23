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
        private readonly GuildBattleParser _parser = new();
        private GuildBattleAssignmentEngine _engine = new();

        public GuildBattleModel(ILogger<GuildBattleModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        [BindProperty]
        public string? GoogleSheetUrl { get; set; }

        [BindProperty]
        public int CurrentDay { get; set; } = 1; // 1..3

        [BindProperty]
        public double MarginOfErrorPercent { get; set; } = 4.0; // default

        [BindProperty]
        public int? Seed { get; set; }

        public GuildBattleParseResult? ParseResult { get; set; }
        public TodayState? Today { get; set; }
        public BattlePlanSummary? BattlePlan { get; set; }
        public string? ErrorMessage { get; set; }
        public List<SheetDefinition> AvailableSheets { get; set; } = new();

        public void OnGet(int? seed, int? day, double? margin)
        {
            // Read query params from Test page seed links
            if (seed.HasValue) Seed = seed.Value;
            if (day.HasValue) CurrentDay = Math.Clamp(day.Value, 1, 3);
            if (margin.HasValue) MarginOfErrorPercent = margin.Value;

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

                Today = today;

                // Use seeded engine if seed is provided
                if (Seed.HasValue)
                {
                    _engine = new GuildBattleAssignmentEngine(Seed.Value);
                }

                // Generate battle plan using the engine
                BattlePlan = _engine.GenerateBattlePlan(players, today, MarginOfErrorPercent);
                
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
    }
}
