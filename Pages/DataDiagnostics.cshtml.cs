using Microsoft.AspNetCore.Mvc.RazorPages;
using FFVIIEverCrisisAnalyzer.Services;
using System.Text.Json;

namespace FFVIIEverCrisisAnalyzer.Pages;

public sealed class DataDiagnosticsModel : PageModel
{
    private readonly WeaponCatalog _catalog;
    private readonly IWebHostEnvironment _env;

    public DataDiagnosticsModel(WeaponCatalog catalog, IWebHostEnvironment env)
    {
        _catalog = catalog;
        _env = env;
    }

    public List<string> TsvItemsMissingAdditionalData { get; private set; } = new();
    public List<string> AdditionalWeaponsMissingFromTsv { get; private set; } = new();
    public List<string> AdditionalOutfitsMissingFromTsv { get; private set; } = new();
    public List<string> AdditionalUltimateWeaponsMissingFromTsv { get; private set; } = new();

    public void OnGet()
    {
        var additionalWeaponNames = LoadAdditionalWeaponNames();
        var additionalOutfitNames = LoadAdditionalOutfitNames();
        var additionalUltimateWeaponNames = LoadAdditionalUltimateWeaponNames();

        var tsvWeaponNames = _catalog.ByWeaponName
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.GachaType) && !kvp.Value.GachaType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tsvUltimateWeaponNames = _catalog.ByWeaponName
            .Where(kvp => kvp.Value.GachaType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tsvCostumeNames = _catalog.ByCostumeName
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in tsvWeaponNames)
        {
            if (tsvUltimateWeaponNames.Contains(name))
            {
                if (!additionalUltimateWeaponNames.Contains(name))
                {
                    TsvItemsMissingAdditionalData.Add($"Ultimate Weapon (TSV) missing additional JSON: {name}");
                }
            }
            else
            {
                if (!additionalWeaponNames.Contains(name))
                {
                    TsvItemsMissingAdditionalData.Add($"Weapon (TSV) missing additional JSON: {name}");
                }
            }
        }

        foreach (var name in tsvCostumeNames)
        {
            if (!additionalOutfitNames.Contains(name))
            {
                TsvItemsMissingAdditionalData.Add($"Costume (TSV) missing additional JSON: {name}");
            }
        }

        foreach (var name in additionalWeaponNames)
        {
            if (!tsvWeaponNames.Contains(name))
            {
                AdditionalWeaponsMissingFromTsv.Add(name);
            }
        }

        foreach (var name in additionalOutfitNames)
        {
            if (!tsvCostumeNames.Contains(name))
            {
                AdditionalOutfitsMissingFromTsv.Add(name);
            }
        }

        foreach (var name in additionalUltimateWeaponNames)
        {
            if (!tsvUltimateWeaponNames.Contains(name))
            {
                AdditionalUltimateWeaponsMissingFromTsv.Add(name);
            }
        }

        TsvItemsMissingAdditionalData = TsvItemsMissingAdditionalData
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AdditionalWeaponsMissingFromTsv = AdditionalWeaponsMissingFromTsv
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AdditionalOutfitsMissingFromTsv = AdditionalOutfitsMissingFromTsv
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private HashSet<string> LoadAdditionalWeaponNames()
    {
        var path = Path.Combine(_env.ContentRootPath, "data", "additionalWeaponData.json");
        if (!System.IO.File.Exists(path))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = System.IO.File.ReadAllText(path);
            var rows = JsonSerializer.Deserialize<List<AdditionalWeaponRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return rows == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    rows.Where(r => !string.IsNullOrWhiteSpace(r.Weapon))
                        .Select(r => r.Weapon!.Trim()),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private HashSet<string> LoadAdditionalOutfitNames()
    {
        var path = Path.Combine(_env.ContentRootPath, "data", "additionalOutfitData.json");
        if (!System.IO.File.Exists(path))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = System.IO.File.ReadAllText(path);
            var rows = JsonSerializer.Deserialize<List<AdditionalOutfitRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return rows == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    rows.Where(r => !string.IsNullOrWhiteSpace(r.Outfit))
                        .Select(r => r.Outfit!.Trim()),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private HashSet<string> LoadAdditionalUltimateWeaponNames()
    {
        var path = Path.Combine(_env.ContentRootPath, "data", "additionalUltimateWeaponData.json");
        if (!System.IO.File.Exists(path))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = System.IO.File.ReadAllText(path);
            var rows = JsonSerializer.Deserialize<List<AdditionalWeaponRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return rows == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    rows.Where(r => !string.IsNullOrWhiteSpace(r.Weapon))
                        .Select(r => r.Weapon!.Trim()),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record AdditionalWeaponRow(string? Character, string? Weapon, string? Ability1, string? Ability2);
    private sealed record AdditionalOutfitRow(
        string? Character,
        string? Outfit,
        string? Command,
        string? Ability1,
        string? Ability2,
        string? Ability3,
        string? Ability4,
        string? Ability5,
        string? Ability6,
        string? Ability7,
        string? Ability8,
        string? Ability9,
        string? Ability10);
}
