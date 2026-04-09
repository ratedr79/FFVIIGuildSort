using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public sealed class SupportTeamBuilderVueModel : PageModel
    {
        private readonly SupportTeamBuilderService _service;

        public SupportTeamBuilderVueModel(SupportTeamBuilderService service)
        {
            _service = service;
        }

        public void OnGet()
        {
        }

        public JsonResult OnGetOptions()
        {
            return new JsonResult(_service.GetOptionData());
        }

        public async Task<IActionResult> OnPostSearchAsync()
        {
            SupportTeamRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<SupportTeamRequest>(Request.Body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return BadRequest(new { error = "Invalid search payload." });
            }

            if (request == null)
            {
                return BadRequest(new { error = "Invalid search payload." });
            }

            request.Filters ??= new List<SupportTeamFilter>();
            if (request.MaxCharacterCount < 1 || request.MaxCharacterCount > 3)
            {
                request.MaxCharacterCount = 2;
            }
            request.MustHaveCharacters = request.MustHaveCharacters?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            request.ExcludeCharacters = request.ExcludeCharacters?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var normalizedOwnedOb = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (request.OwnedObByWeaponId != null)
            {
                foreach (var entry in request.OwnedObByWeaponId)
                {
                    normalizedOwnedOb[entry.Key] = Math.Clamp(entry.Value, 0, 4);
                }
            }

            var normalizedOwnedOutfits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (request.OwnedOutfitById != null)
            {
                foreach (var entry in request.OwnedOutfitById)
                {
                    normalizedOwnedOutfits[entry.Key] = Math.Clamp(entry.Value, 0, 1);
                }
            }

            request.OwnedObByWeaponId = normalizedOwnedOb;
            request.OwnedOutfitById = normalizedOwnedOutfits;

            var result = _service.Search(request);
            return new JsonResult(result);
        }
    }
}
