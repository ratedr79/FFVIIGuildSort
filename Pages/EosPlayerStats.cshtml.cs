using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

// Exports are ~7 MB today; leave generous headroom as accounts grow.
[RequestSizeLimit(64 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 64 * 1024 * 1024)]
public class EosPlayerStatsModel : PageModel
{
    private readonly EosPlayerStatsService _service;
    private readonly ILogger<EosPlayerStatsModel> _logger;

    public EosPlayerStatsModel(EosPlayerStatsService service, ILogger<EosPlayerStatsModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    [BindProperty]
    public bool IncludePurchases { get; set; }

    public EosPlayerStats? Stats { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? FileName { get; private set; }

    public void OnGet()
    {
    }

    public void OnPost()
    {
        if (UploadedFile is null || UploadedFile.Length == 0)
        {
            ErrorMessage = "Choose your EOS export (.json) file first.";
            return;
        }

        FileName = UploadedFile.FileName;
        try
        {
            // Parsed in memory only; the upload is never written to disk.
            using var stream = UploadedFile.OpenReadStream();
            Stats = _service.Parse(stream, IncludePurchases);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            ErrorMessage = ex is InvalidDataException
                ? ex.Message
                : "That file isn't valid JSON. Upload the .json file exported from EOS as-is.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse EOS export {File}", UploadedFile.FileName);
            ErrorMessage = "Something went wrong reading that file.";
        }
    }
}
