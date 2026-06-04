using System;
using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public sealed class SurveyQuickViewCatalogItem
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string EquipmentType { get; init; }
        public required string Character { get; init; }
    }

    public sealed class PlayerInventoryManagementModel : PageModel
    {
        private readonly WeaponSearchDataService _weaponSearchService;
        private readonly IConfiguration _configuration;

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

        public PlayerInventoryManagementModel(WeaponSearchDataService weaponSearchService, IConfiguration configuration)
        {
            _weaponSearchService = weaponSearchService;
            _configuration = configuration;
        }

        public IReadOnlyList<PlayerInventoryCatalogItem> Items { get; private set; } = Array.Empty<PlayerInventoryCatalogItem>();

        public IReadOnlyList<string> AvailableCharacters { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<SheetDefinition> AvailableSurveySheets { get; private set; } = Array.Empty<SheetDefinition>();

        public int DefaultWeaponLevel => 140;

        public int UiMaxWeaponLevel => 140;

        public IReadOnlyList<int> AllowedInventoryLevels { get; } = new[]
        {
            1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140
        };

        public void OnGet()
        {
            LoadAvailableSurveySheets();

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

        public async Task<IActionResult> OnGetSurveyQuickViewAsync([FromQuery] string? sheetUrl)
        {
            LoadAvailableSurveySheets();

            if (string.IsNullOrWhiteSpace(sheetUrl))
            {
                return new JsonResult(new { error = "Please choose a survey sheet." }) { StatusCode = 400 };
            }

            var selectedSheet = AvailableSurveySheets.FirstOrDefault(sheet => sheet.Url.Equals(sheetUrl, StringComparison.OrdinalIgnoreCase));
            if (selectedSheet == null)
            {
                return new JsonResult(new { error = "The selected survey sheet is not configured." }) { StatusCode = 400 };
            }

            try
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(selectedSheet.Url);
                if (!response.IsSuccessStatusCode)
                {
                    return new JsonResult(new { error = $"Failed to download survey sheet: {response.StatusCode}" }) { StatusCode = 502 };
                }

                var csv = await response.Content.ReadAsStringAsync();
                var firstLine = csv
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    return new JsonResult(new { error = "The survey sheet is empty." }) { StatusCode = 400 };
                }

                var headerFields = ParseCsvLine(firstLine)
                    .Select(header => header.Trim().Trim('\uFEFF'))
                    .Where(header => !string.IsNullOrWhiteSpace(header))
                    .ToList();

                var matchedItems = new List<SurveyQuickViewCatalogItem>();
                foreach (var header in headerFields)
                {
                    var matchedItem = _weaponSearchService.TryGetWeaponSearchItemByName(header);
                    if (matchedItem == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(matchedItem.EquipmentType))
                    {
                        continue;
                    }

                    matchedItems.Add(new SurveyQuickViewCatalogItem
                    {
                        Id = matchedItem.Id,
                        Name = matchedItem.Name,
                        EquipmentType = matchedItem.EquipmentType,
                        Character = matchedItem.Character
                    });
                }

                return new JsonResult(new
                {
                    sheetName = selectedSheet.Name,
                    items = matchedItems
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = $"Failed to read survey sheet CSV: {ex.Message}" }) { StatusCode = 500 };
            }
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

        private void LoadAvailableSurveySheets()
        {
            var configuredSheets = _configuration.GetSection("GoogleSheets:SurveySheets")
                .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AvailableSurveySheets = configuredSheets
                .Where(sheet => !string.IsNullOrWhiteSpace(sheet.Name) && !string.IsNullOrWhiteSpace(sheet.Url))
                .Where(sheet => seenKeys.Add($"{sheet.Name.Trim()}|{sheet.Url.Trim()}"))
                .ToList();
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            fields.Add(current.ToString());
            return fields;
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
