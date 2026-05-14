using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

public sealed class GuildBattleSheetModel : PageModel
{
    private readonly GuildBattleSheetService _sheetService;
    private readonly WeaponSearchDataService _weaponSearchService;

    public GuildBattleSheetModel(GuildBattleSheetService sheetService, WeaponSearchDataService weaponSearchService)
    {
        _sheetService = sheetService;
        _weaponSearchService = weaponSearchService;
    }

    public GuildBattleSheetViewModel? Sheet { get; private set; }
    public int DefaultWeaponLevel => 140;
    public int UiMaxWeaponLevel => 140;

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Debug { get; set; }

    [BindProperty(SupportsGet = true)]
    public GuildBattleSheetRecommendationMode Mode { get; set; } = GuildBattleSheetRecommendationMode.Traditional;

    public IActionResult OnGet()
    {
        Sheet = _sheetService.BuildSheet(Id, Debug, Mode);
        if (Sheet == null)
        {
            ModelState.AddModelError(string.Empty, "No guild battle sheet data is configured.");
        }

        return Page();
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
}
