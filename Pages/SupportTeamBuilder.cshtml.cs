using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public sealed class SupportTeamBuilderModel : PageModel
    {
        private readonly SupportTeamBuilderService _service;

        public SupportTeamBuilderModel(SupportTeamBuilderService service)
        {
            _service = service;
        }

        [BindProperty]
        public List<SupportTeamFilter> Filters { get; set; } = new() { new SupportTeamFilter() };

        [BindProperty]
        public int MaxCharacterCount { get; set; } = 2;

        [BindProperty]
        public List<string> MustHaveCharacters { get; set; } = new();

        [BindProperty]
        public List<string> ExcludeCharacters { get; set; } = new();

        [BindProperty]
        public string OwnedObJson { get; set; } = "{}";

        [BindProperty]
        public string OwnedOutfitJson { get; set; } = "{}";

        public SupportTeamBuilderOptionData Options { get; private set; } = new();
        public SupportTeamBuilderResponse? Result { get; private set; }

        public void OnGet()
        {
            LoadOptions();
        }

        public void OnPostSearch()
        {
            LoadOptions();

            if (MaxCharacterCount < 1 || MaxCharacterCount > 3)
            {
                MaxCharacterCount = 2;
            }

            var request = new SupportTeamRequest
            {
                Filters = Filters,
                MaxCharacterCount = MaxCharacterCount,
                MustHaveCharacters = MustHaveCharacters.ToHashSet(StringComparer.OrdinalIgnoreCase),
                ExcludeCharacters = ExcludeCharacters.ToHashSet(StringComparer.OrdinalIgnoreCase),
                OwnedObByWeaponId = DeserializeOwnedOb(OwnedObJson),
                OwnedOutfitById = DeserializeOwnedOutfits(OwnedOutfitJson)
            };

            Result = _service.Search(request);
        }

        private void LoadOptions()
        {
            Options = _service.GetOptionData();

            if (Filters.Count == 0)
            {
                Filters.Add(new SupportTeamFilter());
            }
        }

        private static Dictionary<string, int> DeserializeOwnedOb(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (parsed == null)
                {
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }

                return new Dictionary<string, int>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, int> DeserializeOwnedOutfits(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (parsed == null)
                {
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }

                var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in parsed)
                {
                    normalized[entry.Key] = Math.Clamp(entry.Value, 0, 1);
                }

                return normalized;
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
