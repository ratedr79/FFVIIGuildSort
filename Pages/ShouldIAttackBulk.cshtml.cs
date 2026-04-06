using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class ShouldIAttackBulkModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly GuildBattleParser _parser = new();
        private readonly ShouldIAttackService _service;
        private readonly ILogger<ShouldIAttackBulkModel> _logger;

        public ShouldIAttackBulkModel(
            IConfiguration configuration,
            ShouldIAttackService service,
            ILogger<ShouldIAttackBulkModel> logger)
        {
            _configuration = configuration;
            _service = service;
            _logger = logger;
        }

        [BindProperty]
        public string GoogleSheetUrl { get; set; } = string.Empty;

        [BindProperty]
        public int NumberOfRuns { get; set; } = 5;

        [BindProperty]
        public string SeedMode { get; set; } = "Auto";

        public List<SheetDefinition> AvailableSheets { get; set; } = new();
        public List<ShouldIAttackBulkRow> Rows { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public int CurrentDay { get; set; } = 1;

        public void OnGet()
        {
            LoadSheets();
        }

        public async Task<IActionResult> OnPostAnalyzeAsync()
        {
            LoadSheets();

            if (string.IsNullOrWhiteSpace(GoogleSheetUrl))
            {
                ErrorMessage = "Please select a guild sheet.";
                return Page();
            }

            var players = await ParsePlayersFromSheetAsync(GoogleSheetUrl);
            if (players == null || players.Count == 0)
            {
                ErrorMessage = "No player data found in selected sheet.";
                return Page();
            }

            CurrentDay = ShouldIAttackService.DetectCurrentDay(players);
            var today = TodayStateBuilder.BuildTodayState(players, CurrentDay, TodayStateBuildMode.OrderAgnosticAggregate, out _);
            Rows = await _service.AnalyzeAllPlayersAsync(players, today, GoogleSheetUrl, HttpContext.RequestAborted);
            return Page();
        }

        private void LoadSheets()
        {
            AvailableSheets = _configuration.GetSection("GoogleSheets:GuildBattleSheets")
                .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();
        }

        private async Task<List<PlayerStageProfile>?> ParsePlayersFromSheetAsync(string sheetUrl)
        {
            try
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(sheetUrl, HttpContext.RequestAborted);
                if (!response.IsSuccessStatusCode)
                {
                    ErrorMessage = $"Failed to download Google Sheet: {response.StatusCode}";
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                var parse = await _parser.ParseAsync(stream);
                return parse.Players;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse guild sheet for ShouldIAttack bulk diagnostics");
                ErrorMessage = $"Failed to parse selected sheet: {ex.Message}";
                return null;
            }
        }
    }
}
