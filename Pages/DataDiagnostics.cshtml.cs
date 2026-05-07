using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Hosting;
using FFVIIEverCrisisAnalyzer.Services;
using FFVIIEverCrisisAnalyzer.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace FFVIIEverCrisisAnalyzer.Pages;

public sealed class DataDiagnosticsModel : PageModel
{
    private readonly WeaponCatalog _catalog;
    private readonly WeaponSearchDataService _weaponSearchData;
    private readonly SharedAccessGate _sharedAccessGate;
    private readonly IWebHostEnvironment _environment;

    public DataDiagnosticsModel(WeaponCatalog catalog, WeaponSearchDataService weaponSearchData, SharedAccessGate sharedAccessGate, IWebHostEnvironment environment)
    {
        _catalog = catalog;
        _weaponSearchData = weaponSearchData;
        _sharedAccessGate = sharedAccessGate;
        _environment = environment;
    }

    public List<string> WeaponsNotEnriched { get; private set; } = new();
    public List<string> WeaponsEnriched { get; private set; } = new();
    public List<string> CostumesNotEnriched { get; private set; } = new();
    public List<string> CostumesEnriched { get; private set; } = new();
    public List<LocalImageDiagnosticRow> MissingWeaponImages { get; private set; } = new();
    public List<LocalImageDiagnosticRow> MissingCostumeImages { get; private set; } = new();
    public List<WeaponSearchDataService.PassiveSkillTypeDiagnosticRow> PassiveSkillTypeDiagnostics { get; private set; } = new();
    public List<CharacterIdGapDiagnosticGroup> CharacterIdGapDiagnostics { get; private set; } = new();
    public bool ReloadSucceeded { get; private set; }
    public string ReloadMessage { get; private set; } = string.Empty;
    public DateTimeOffset LastLoadedUtc => _weaponSearchData.LastLoadedUtc;
    public int ReloadCount => _weaponSearchData.ReloadCount;
    public int TotalWeaponItemsWithImages { get; private set; }
    public int TotalCostumeItemsWithImages { get; private set; }
    public int WeaponImageAssetCount { get; private set; }
    public int CostumeImageAssetCount { get; private set; }

    private static readonly Regex InvalidAssetFilenameCharactersRegex = new(@"[<>:""/\\|?*]+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public void OnGet()
    {
        var allItems = _weaponSearchData.GetWeapons();

        foreach (var kvp in _catalog.ByWeaponName.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var weapon = kvp.Value;
            var label = $"{weapon.Name} ({weapon.Character}, {weapon.EquipmentType})";
            if (weapon.GearSearchEnriched)
            {
                var potInfo = weapon.PotPercentByOb.Count > 0
                    ? $"Pot% OB0={weapon.PotPercentByOb.GetValueOrDefault(0):0.#}% OB10={weapon.PotPercentByOb.GetValueOrDefault(10):0.#}%"
                    : "No pot data";
                var rAbilCount = weapon.GearSearchRAbilities.Count;
                var custCount = weapon.CustomizationDescriptions.Count;
                WeaponsEnriched.Add($"{label} — {potInfo}, {rAbilCount} R Abilities, {custCount} Customizations");
            }
            else
            {
                WeaponsNotEnriched.Add(label);
            }
        }

        foreach (var kvp in _catalog.ByCostumeName.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var costume = kvp.Value;
            var label = $"{costume.Name} ({costume.Character})";
            if (costume.GearSearchEnriched)
            {
                var rAbilCount = costume.GearSearchRAbilities.Count;
                CostumesEnriched.Add($"{label} — {rAbilCount} R Abilities");
            }
            else
            {
                CostumesNotEnriched.Add(label);
            }
        }

        BuildLocalImageDiagnostics(allItems);
        PassiveSkillTypeDiagnostics = _weaponSearchData.GetPassiveSkillTypeDiagnostics().ToList();
        CharacterIdGapDiagnostics = BuildCharacterIdGapDiagnostics(allItems);
    }

    public IActionResult OnPostReloadData()
    {
        if (!Request.Cookies.TryGetValue(_sharedAccessGate.CookieName, out var token) || !_sharedAccessGate.IsValidToken(token))
        {
            return Forbid();
        }

        try
        {
            _weaponSearchData.ReloadData();
            _catalog.RefreshFromGearSearch();
            ReloadSucceeded = true;
            ReloadMessage = $"Reload complete ({_weaponSearchData.ReloadCount} total reloads).";
        }
        catch (Exception ex)
        {
            ReloadSucceeded = false;
            ReloadMessage = $"Reload failed: {ex.Message}";
        }

        OnGet();
        return Page();
    }

    private void BuildLocalImageDiagnostics(IReadOnlyList<WeaponSearchItem> allItems)
    {
        var weaponImageLookup = LoadLocalImageLookup("images/weapons", out var weaponAssetCount);
        var costumeImageLookup = LoadLocalImageLookup("images/outfits", out var costumeAssetCount);

        WeaponImageAssetCount = weaponAssetCount;
        CostumeImageAssetCount = costumeAssetCount;

        var weaponItems = allItems
            .Where(item => !item.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var costumeItems = allItems
            .Where(item => item.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
            .ToList();

        TotalWeaponItemsWithImages = weaponItems.Count;
        TotalCostumeItemsWithImages = costumeItems.Count;

        MissingWeaponImages = BuildMissingLocalImageRows(weaponItems, weaponImageLookup, "/images/weapons");
        MissingCostumeImages = BuildMissingLocalImageRows(costumeItems, costumeImageLookup, "/images/outfits");
    }

    private static List<LocalImageDiagnosticRow> BuildMissingLocalImageRows(
        IEnumerable<WeaponSearchItem> items,
        IReadOnlyDictionary<string, string> availableImages,
        string folderPath)
    {
        return items
            .Where(item => !availableImages.ContainsKey(NormalizeAssetLookupKey(item.Name)))
            .OrderBy(item => item.Character, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new LocalImageDiagnosticRow
            {
                Character = item.Character,
                Name = item.Name,
                EquipmentType = item.EquipmentType,
                FolderPath = folderPath,
                SuggestedFileName = BuildSuggestedFileName(item.Name)
            })
            .ToList();
    }

    private Dictionary<string, string> LoadLocalImageLookup(string relativeFolder, out int assetCount)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        assetCount = 0;

        foreach (var folderPath in GetCandidateAssetDirectories(relativeFolder))
        {
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            var files = Directory
                .EnumerateFiles(folderPath)
                .Where(IsSupportedImageFile)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            assetCount = files.Count;
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                var key = NormalizeAssetLookupKey(Path.GetFileNameWithoutExtension(fileName));
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                lookup.TryAdd(key, fileName);
            }

            break;
        }

        return lookup;
    }

    private IEnumerable<string> GetCandidateAssetDirectories(string relativeFolder)
    {
        var normalizedRelativeFolder = relativeFolder.Replace('/', Path.DirectorySeparatorChar);
        var bases = new[]
        {
            _environment.WebRootPath,
            string.IsNullOrWhiteSpace(_environment.ContentRootPath)
                ? null
                : Path.Combine(_environment.ContentRootPath, "wwwroot")
        };

        foreach (var basePath in bases
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(basePath!, normalizedRelativeFolder);
        }
    }

    private static bool IsSupportedImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = InvalidAssetFilenameCharactersRegex.Replace(value.Trim(), " ");
        normalized = normalized.Replace('_', ' ');
        normalized = WhitespaceRegex.Replace(normalized, " ").Trim();
        return normalized.ToUpperInvariant();
    }

    private static string BuildSuggestedFileName(string name)
    {
        var sanitized = InvalidAssetFilenameCharactersRegex.Replace(name.Trim(), "_");
        sanitized = WhitespaceRegex.Replace(sanitized, " ").Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized)
            ? "unnamed.png"
            : $"{sanitized}.png";
    }

    private static List<CharacterIdGapDiagnosticGroup> BuildCharacterIdGapDiagnostics(IReadOnlyList<WeaponSearchItem> allItems)
    {
        return allItems
            .GroupBy(item => item.Character, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var costumeItems = group.Where(item => item.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase));
                var weaponItems = group.Where(item => !item.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase));

                return new CharacterIdGapDiagnosticGroup
                {
                    CharacterName = group.Key,
                    CostumeRows = BuildIdRows(costumeItems),
                    WeaponRows = BuildIdRows(weaponItems)
                };
            })
            .ToList();
    }

    private static List<CharacterIdGapDiagnosticRow> BuildIdRows(IEnumerable<FFVIIEverCrisisAnalyzer.Models.WeaponSearchItem> items)
    {
        var idToName = items
            .Select(item =>
            {
                var parsed = TryExtractNumericId(item.Id, out var id);
                return new { Parsed = parsed, Id = id, item.Name };
            })
            .Where(entry => entry.Parsed)
            .GroupBy(entry => entry.Id)
            .OrderBy(group => group.Key)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(x => x.Name)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? string.Empty);

        if (idToName.Count == 0)
        {
            return new List<CharacterIdGapDiagnosticRow>();
        }

        var minId = idToName.Keys.Min();
        var maxId = idToName.Keys.Max();
        var rows = new List<CharacterIdGapDiagnosticRow>(maxId - minId + 1);
        for (var currentId = minId; currentId <= maxId; currentId++)
        {
            if (idToName.TryGetValue(currentId, out var name))
            {
                rows.Add(new CharacterIdGapDiagnosticRow
                {
                    Id = currentId,
                    Name = name,
                    IsMissing = false
                });

                continue;
            }

            rows.Add(new CharacterIdGapDiagnosticRow
            {
                Id = currentId,
                Name = "MISSING",
                IsMissing = true
            });
        }

        return rows;
    }

    private static bool TryExtractNumericId(string? rawId, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return false;
        }

        if (int.TryParse(rawId, out id))
        {
            return true;
        }

        var segments = rawId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        return int.TryParse(segments[^1], out id);
    }

    public sealed class CharacterIdGapDiagnosticGroup
    {
        public string CharacterName { get; set; } = string.Empty;
        public List<CharacterIdGapDiagnosticRow> CostumeRows { get; set; } = new();
        public List<CharacterIdGapDiagnosticRow> WeaponRows { get; set; } = new();
        public int MissingCostumeCount => CostumeRows.Count(row => row.IsMissing);
        public int MissingWeaponCount => WeaponRows.Count(row => row.IsMissing);
    }

    public sealed class CharacterIdGapDiagnosticRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsMissing { get; set; }
    }

    public sealed class LocalImageDiagnosticRow
    {
        public string Character { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string EquipmentType { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string SuggestedFileName { get; set; } = string.Empty;
    }
}
