using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

public sealed class UnlockModel : PageModel
{
    private readonly SharedAccessGate _gate;

    public UnlockModel(SharedAccessGate gate)
    {
        _gate = gate;
    }

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    public void OnGet()
    {
        ReturnUrl = _gate.SanitizeReturnUrl(ReturnUrl);
    }

    public IActionResult OnPost()
    {
        ReturnUrl = _gate.SanitizeReturnUrl(ReturnUrl);

        if (!_gate.ValidatePassword(Password))
        {
            ModelState.AddModelError(nameof(Password), "Incorrect password.");
            return Page();
        }

        var token = _gate.GenerateToken();
        Response.Cookies.Append(_gate.CookieName, token, _gate.BuildCookieOptions());

        return LocalRedirect(ReturnUrl);
    }
}
