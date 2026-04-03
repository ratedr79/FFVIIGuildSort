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
        private readonly EnemyCatalog _enemyCatalog;

        public EnemyStatsModel(EnemyCatalog enemyCatalog)
        {
            _enemyCatalog = enemyCatalog;
        }

        [BindProperty]
        public string? SearchQuery { get; set; }

        public IReadOnlyList<string> SearchSuggestions { get; private set; } = new List<string>();

        public List<EnemySearchResult> Results { get; private set; } = new();

        public Dictionary<string, EnemyDetailView> DetailsByKey { get; private set; } = new();

        public bool HasSearchAttempt { get; private set; }

        public bool ShowNoResults => HasSearchAttempt && Results.Count == 0 && ModelState.IsValid;

        public void OnGet()
        {
            SearchSuggestions = _enemyCatalog.GetSearchSuggestions();
        }

        public IActionResult OnPostSearch()
        {
            HasSearchAttempt = true;
            SearchSuggestions = _enemyCatalog.GetSearchSuggestions();

            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                ModelState.AddModelError(nameof(SearchQuery), "Please enter a boss name to search.");
                return Page();
            }

            Results = _enemyCatalog.SearchEnemies(SearchQuery).ToList();
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
    }
}
