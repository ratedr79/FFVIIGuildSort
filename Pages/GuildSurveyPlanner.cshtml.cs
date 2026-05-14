using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

public sealed class GuildSurveyPlannerModel : PageModel
{
    private readonly GuildSurveyPlannerService _plannerService;

    public GuildSurveyPlannerModel(GuildSurveyPlannerService plannerService)
    {
        _plannerService = plannerService;
    }

    public GuildSurveyPlannerViewModel? Planner { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Element { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AbilityType { get; set; }

    public IActionResult OnGet()
    {
        Planner = _plannerService.Build(Element, AbilityType);
        Element = Planner.SelectedElement;
        AbilityType = Planner.SelectedAbilityType;
        return Page();
    }
}
