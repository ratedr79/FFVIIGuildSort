using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class ShouldIAttackModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly GuildBattleParser _parser = new();
        private readonly ShouldIAttackService _service;
        private readonly SharedAccessGate _sharedAccessGate;
        private readonly ILogger<ShouldIAttackModel> _logger;

        public ShouldIAttackModel(
            IConfiguration configuration,
            ShouldIAttackService service,
            SharedAccessGate sharedAccessGate,
            ILogger<ShouldIAttackModel> logger)
        {
            _configuration = configuration;
            _service = service;
            _sharedAccessGate = sharedAccessGate;
            _logger = logger;
        }

        [BindProperty]
        public string GoogleSheetUrl { get; set; } = string.Empty;

        [BindProperty]
        public string PlayerName { get; set; } = string.Empty;

        [BindProperty]
        public bool UseAttacksNow { get; set; }

        [BindProperty]
        public int SelectedDay { get; set; } = 1;

        public List<SheetDefinition> AvailableSheets { get; set; } = new();
        public List<string> AvailablePlayers { get; set; } = new();
        public ShouldIAttackRecommendationResult? Recommendation { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsLeadershipUnlocked { get; private set; }
        public int DetectedDaySuggestion { get; private set; } = 1;

        public void OnGet()
        {
            LoadSheets();
            IsLeadershipUnlocked = GetLeadershipUnlocked();
        }

        public async Task<IActionResult> OnPostLoadPlayersAsync()
        {
            LoadSheets();
            IsLeadershipUnlocked = GetLeadershipUnlocked();

            if (string.IsNullOrWhiteSpace(GoogleSheetUrl))
            {
                return new JsonResult(new { players = Array.Empty<string>(), detectedDay = 1 });
            }

            var parsed = await ParsePlayersFromSheetAsync(GoogleSheetUrl);
            if (parsed == null)
            {
                return new JsonResult(new { players = Array.Empty<string>(), detectedDay = 1 });
            }

            var detectedDay = ShouldIAttackService.DetectCurrentDay(parsed);
            DetectedDaySuggestion = detectedDay;

            var names = parsed
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new JsonResult(new { players = names, detectedDay });
        }

        public async Task<IActionResult> OnPostAnalyzeAsync()
        {
            LoadSheets();
            IsLeadershipUnlocked = GetLeadershipUnlocked();

            if (string.IsNullOrWhiteSpace(GoogleSheetUrl))
            {
                ErrorMessage = "Please select a guild sheet.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(PlayerName))
            {
                ErrorMessage = "Please select a player.";
                return Page();
            }

            var players = await ParsePlayersFromSheetAsync(GoogleSheetUrl);
            if (players == null || players.Count == 0)
            {
                ErrorMessage = "No player data found in selected sheet.";
                return Page();
            }

            AvailablePlayers = players
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!AvailablePlayers.Contains(PlayerName, StringComparer.OrdinalIgnoreCase))
            {
                ErrorMessage = "Selected player was not found in the selected sheet.";
                return Page();
            }

            var detectedDay = ShouldIAttackService.DetectCurrentDay(players);
            DetectedDaySuggestion = detectedDay;

            if (SelectedDay < 1 || SelectedDay > 3)
            {
                SelectedDay = detectedDay;
            }

            var today = TodayStateBuilder.BuildTodayState(players, SelectedDay, TodayStateBuildMode.OrderAgnosticAggregate, out _);
            Recommendation = await _service.AnalyzePlayerAsync(players, today, GoogleSheetUrl, PlayerName, UseAttacksNow, HttpContext.RequestAborted);
            return Page();
        }

        private bool GetLeadershipUnlocked()
        {
            if (!Request.Cookies.TryGetValue(_sharedAccessGate.CookieName, out var token))
            {
                return false;
            }

            return _sharedAccessGate.IsValidToken(token);
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
                _logger.LogError(ex, "Failed to parse guild sheet for ShouldIAttack");
                ErrorMessage = $"Failed to parse selected sheet: {ex.Message}";
                return null;
            }
        }
    }
}
