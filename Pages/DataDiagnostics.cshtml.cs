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
}
