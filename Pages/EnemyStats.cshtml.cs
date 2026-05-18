using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class EnemyStatsModel : PageModel
    {
        public const string BattleModeAll = "all";
        public const string BattleModeSolo = "solo";
        public const string BattleModeCoop = "coop";

        private readonly EnemyCatalog _enemyCatalog;

        public EnemyStatsModel(EnemyCatalog enemyCatalog)
        {
            _enemyCatalog = enemyCatalog;
        }

        [BindProperty]
        public string? SearchQuery { get; set; }

        [BindProperty]
        public string SearchMode { get; set; } = BattleModeAll;

        public IReadOnlyList<string> SearchSuggestions { get; private set; } = new List<string>();

        public List<EnemySearchResult> Results { get; private set; } = new();

        public Dictionary<string, EnemyDetailView> DetailsByKey { get; private set; } = new();

        public bool HasSearchAttempt { get; private set; }

        public bool ShowNoResults => HasSearchAttempt && Results.Count == 0 && ModelState.IsValid;

        public void OnGet()
        {
            SearchMode = NormalizeBattleMode(SearchMode);
            SearchSuggestions = _enemyCatalog.GetSearchSuggestions(SearchMode);
        }

        public IActionResult OnPostSearch()
        {
            HasSearchAttempt = true;
            SearchMode = NormalizeBattleMode(SearchMode);
            SearchSuggestions = _enemyCatalog.GetSearchSuggestions(SearchMode);

            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                ModelState.AddModelError(nameof(SearchQuery), "Please enter a boss name to search.");
                return Page();
            }

            Results = _enemyCatalog.SearchEnemies(SearchQuery, SearchMode).ToList();
            DetailsByKey = new Dictionary<string, EnemyDetailView>();

            foreach (var result in Results)
            {
                var detail = _enemyCatalog.GetEnemyDetails(result.EnemyId, result.Level);
                if (detail != null)
                {
                    DetailsByKey[result.Key] = detail;
                }
            }

            return Page();
        }

        public EnemyDetailView? FindDetail(EnemySearchResult result)
        {
            return DetailsByKey.TryGetValue(result.Key, out var detail) ? detail : null;
        }

        private static string NormalizeBattleMode(string? mode)
        {
            if (string.Equals(mode, BattleModeSolo, System.StringComparison.OrdinalIgnoreCase))
            {
                return BattleModeSolo;
            }

            if (string.Equals(mode, BattleModeCoop, System.StringComparison.OrdinalIgnoreCase))
            {
                return BattleModeCoop;
            }

            return BattleModeAll;
        }
    }
}
