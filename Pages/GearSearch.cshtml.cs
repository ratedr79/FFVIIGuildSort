using System;
using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class GearSearchModel : PageModel
    {
        private readonly WeaponSearchDataService _stockService;
        private readonly IServiceProvider _services;
        private EcrWeaponSearchDataService? _ecrService;

        public GearSearchModel(WeaponSearchDataService weaponSearchService, IServiceProvider services)
        {
            _stockService = weaponSearchService;
            _services = services;
        }

        /// <summary>"ecr" shows the ECR (Reawakened) Rebalancing data instead of the stock game data.</summary>
        [BindProperty(SupportsGet = true)]
        public string? Data { get; set; }

        public bool IsEcr => string.Equals(Data, "ecr", StringComparison.OrdinalIgnoreCase);

        // The ECR data is only loaded the first time someone opens the ECR Gear Search.
        public EcrWeaponSearchDataService EcrService => _ecrService ??= _services.GetRequiredService<EcrWeaponSearchDataService>();

        private WeaponSearchDataService _weaponSearchService => IsEcr ? EcrService : _stockService;

        /// <summary>Gear Search item id -> the ECR changes that touch it (ECR view only).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<EcrOverlayStore.Change>> EcrChangesByGearId =>
            IsEcr ? EcrService.Overlay.ChangesByGearId : new Dictionary<string, IReadOnlyList<EcrOverlayStore.Change>>();

        [BindProperty(SupportsGet = true)]
        public string? CharacterFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SearchText { get; set; }

        [BindProperty(SupportsGet = true)]
        public new int Page { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 50;

        public IReadOnlyList<WeaponSearchItem> Weapons { get; private set; } = new List<WeaponSearchItem>();
        public int DefaultWeaponLevel => 140;
        public int UiMaxWeaponLevel => 140;
        public int MaxWeaponLevel => _weaponSearchService.MaxWeaponLevel;
        public int TotalItems { get; private set; }
        public int TotalPages => 1;
        public bool HasPreviousPage => false;
        public bool HasNextPage => false;

        // Available characters for filter
        public static readonly List<string> AvailableCharacters = new()
        {
            "Cloud", "Barret", "Tifa", "Aerith", "Red XIII", "Yuffie", "Cait Sith", "Vincent",
            "Cid", "Zack", "Sephiroth", "Glenn", "Matt", "Lucia", "Angeal", "Sephiroth (Original)"
        };

        // Available page sizes
        public static readonly List<int> AvailablePageSizes = new() { 25, 50, 100, 200 };

        public void OnGet()
        {
            // Validate page size selection even though DataTables handles pagination client-side
            if (!AvailablePageSizes.Contains(PageSize))
                PageSize = 50;

            if (IsEcr)
                EcrService.RefreshFromRecipe();

            var allWeapons = _weaponSearchService.SearchWeapons(SearchText ?? string.Empty, CharacterFilter);
            TotalItems = allWeapons.Count;
            Weapons = allWeapons;
            Page = 1;
            PageSize = Math.Max(PageSize, 1);
        }

        public IActionResult OnGetWeaponSnapshot(string weaponId, int overboost, int level)
        {
            var snapshot = _weaponSearchService.GetWeaponSnapshot(weaponId, overboost, level);
            if (snapshot == null)
                return new JsonResult(new { error = "Weapon not found" }) { StatusCode = 404 };

            return new JsonResult(snapshot);
        }
    }
}
