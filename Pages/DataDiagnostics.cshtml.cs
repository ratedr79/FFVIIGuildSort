using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FFVIIEverCrisisAnalyzer.Services;

namespace FFVIIEverCrisisAnalyzer.Pages;

public sealed class DataDiagnosticsModel : PageModel
{
    private readonly WeaponCatalog _catalog;
    private readonly WeaponSearchDataService _weaponSearchData;
    private readonly SharedAccessGate _sharedAccessGate;

    public DataDiagnosticsModel(WeaponCatalog catalog, WeaponSearchDataService weaponSearchData, SharedAccessGate sharedAccessGate)
    {
        _catalog = catalog;
        _weaponSearchData = weaponSearchData;
        _sharedAccessGate = sharedAccessGate;
    }

    public List<string> WeaponsNotEnriched { get; private set; } = new();
    public List<string> WeaponsEnriched { get; private set; } = new();
    public List<string> CostumesNotEnriched { get; private set; } = new();
    public List<string> CostumesEnriched { get; private set; } = new();
    public List<WeaponSearchDataService.PassiveSkillTypeDiagnosticRow> PassiveSkillTypeDiagnostics { get; private set; } = new();
    public List<CharacterIdGapDiagnosticGroup> CharacterIdGapDiagnostics { get; private set; } = new();
    public bool ReloadSucceeded { get; private set; }
    public string ReloadMessage { get; private set; } = string.Empty;
    public DateTimeOffset LastLoadedUtc => _weaponSearchData.LastLoadedUtc;
    public int ReloadCount => _weaponSearchData.ReloadCount;

    public void OnGet()
    {
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

        PassiveSkillTypeDiagnostics = _weaponSearchData.GetPassiveSkillTypeDiagnostics().ToList();
        CharacterIdGapDiagnostics = BuildCharacterIdGapDiagnostics();
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

    private List<CharacterIdGapDiagnosticGroup> BuildCharacterIdGapDiagnostics()
    {
        var allItems = _weaponSearchData.GetWeapons();
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
}
