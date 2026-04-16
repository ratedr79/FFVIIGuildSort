using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public sealed class DamageCalcModel : PageModel
    {
        private readonly DamageCalcService _damageCalcService;

        public DamageCalcModel(DamageCalcService damageCalcService)
        {
            _damageCalcService = damageCalcService;
        }

        [BindProperty]
        public DamageCalcRequest Input { get; set; } = new();

        public DamageCalcResult? Result { get; private set; }

        public void OnGet()
        {
        }

        public IActionResult OnPostCalculate()
        {
            Result = _damageCalcService.Calculate(Input);
            return Page();
        }
    }
}
