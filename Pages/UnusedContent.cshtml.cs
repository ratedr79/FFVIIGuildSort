using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

public class UnusedContentModel : PageModel
{
    private readonly UnusedContentService _service;

    public UnusedContentModel(UnusedContentService service) => _service = service;

    public UnusedContentReport Report { get; private set; } = new();

    public void OnGet() => Report = _service.Report;
}
