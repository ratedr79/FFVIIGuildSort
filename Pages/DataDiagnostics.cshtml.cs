using Microsoft.AspNetCore.Mvc.RazorPages;
using FFVIIEverCrisisAnalyzer.Services;

namespace FFVIIEverCrisisAnalyzer.Pages;

public sealed class DataDiagnosticsModel : PageModel
{
    private readonly WeaponCatalog _catalog;

    public DataDiagnosticsModel(WeaponCatalog catalog)
    {
        _catalog = catalog;
    }

    public List<string> WeaponsNotEnriched { get; private set; } = new();
    public List<string> WeaponsEnriched { get; private set; } = new();
    public List<string> CostumesNotEnriched { get; private set; } = new();
    public List<string> CostumesEnriched { get; private set; } = new();

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
    }
}
