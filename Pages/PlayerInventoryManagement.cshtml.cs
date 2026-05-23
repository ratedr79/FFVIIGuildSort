using System;
using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public sealed class PlayerInventoryManagementModel : PageModel
    {
        private readonly WeaponSearchDataService _weaponSearchService;

        private static readonly Dictionary<string, string> CharacterPortraits = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Aerith"] = "Aerith.jpg",
            ["Angeal"] = "Angeal.jpg",
            ["Barret"] = "Barret.jpg",
            ["Cait Sith"] = "Cait Sith.jpg",
            ["Cid"] = "Cid.jpg",
            ["Cloud"] = "Cloud.jpg",
            ["Glenn"] = "Glenn.jpg",
            ["Lucia"] = "Lucia.jpg",
            ["Matt"] = "Matt.jpg",
            ["Red XIII"] = "Red XIII.jpg",
            ["Sephiroth"] = "Sephiroth.jpg",
            ["Sephiroth (Original)"] = "Sephiroth (Original).jpg",
            ["Tifa"] = "Tifa.jpg",
            ["Vincent"] = "Vincent.jpg",
            ["Yuffie"] = "Yuffie.jpg",
            ["Zack"] = "Zack.jpg"
        };

        public PlayerInventoryManagementModel(WeaponSearchDataService weaponSearchService)
        {
            _weaponSearchService = weaponSearchService;
        }

        public IReadOnlyList<PlayerInventoryCatalogItem> Items { get; private set; } = Array.Empty<PlayerInventoryCatalogItem>();

        public IReadOnlyList<string> AvailableCharacters { get; private set; } = Array.Empty<string>();

        public int DefaultWeaponLevel => 140;

        public int UiMaxWeaponLevel => 140;

        public IReadOnlyList<int> AllowedInventoryLevels { get; } = new[]
        {
            1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140
        };

        public void OnGet()
        {
            var allItems = _weaponSearchService
                .GetWeapons()
                .OrderBy(item => item.CharacterId)
                .ThenBy(item => item.EquipmentType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Items = allItems
                .Select(item => new PlayerInventoryCatalogItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    CharacterId = item.CharacterId,
                    Character = item.Character,
                    CharacterPortraitUrl = ResolveCharacterPortraitUrl(item.Character),
                    ImageUrl = item.ImageUrl,
                    PreviewImageUrl = item.PreviewImageUrl,
                    EquipmentType = item.EquipmentType,
                    Element = item.Element,
                    AbilityType = item.AbilityType,
                    Range = item.Range,
                    AbilityText = item.AbilityText,
                    HasCustomizations = item.Customizations.Count > 0,
                    SupportsViewLevels = !string.Equals(item.EquipmentType, "Costume", StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            AvailableCharacters = Items
                .Select(item => item.Character)
                .Where(character => !string.IsNullOrWhiteSpace(character))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(character => character, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IActionResult OnGetWeaponSnapshot(string weaponId, int overboost, int level)
        {
            var snapshot = _weaponSearchService.GetWeaponSnapshot(weaponId, overboost, level);
            if (snapshot == null)
            {
                return new JsonResult(new { error = "Weapon not found" }) { StatusCode = 404 };
            }

            return new JsonResult(snapshot);
        }

        private static string ResolveCharacterPortraitUrl(string character)
        {
            if (CharacterPortraits.TryGetValue(character ?? string.Empty, out var portraitFileName))
            {
                return $"/images/characters/sm/{Uri.EscapeDataString(portraitFileName)}";
            }

            return "/images/characters/sm/Cloud.jpg";
        }
    }
}
