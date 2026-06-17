using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    // Backend for the Interactive Team Builder page: the user manually picks a fixed 3-character team and sees its
    // damage/score (the SAME number the V2 analyzer reports). The Vue UI is added separately; this model only
    // serves the slot catalog and scores posted team specs. All scoring reuses PlayerPowerAnalyzerV2Service.
    public sealed class InteractiveTeamBuilderModel : PageModel
    {
        private readonly PlayerPowerAnalyzerV2Service _analyzer;

        private static readonly JsonSerializerOptions ScoreRequestOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public InteractiveTeamBuilderModel(PlayerPowerAnalyzerV2Service analyzer)
        {
            _analyzer = analyzer;
        }

        public void OnGet()
        {
        }

        // Per-character slot catalog (all weapon/ultimate/costume options + the full sub-weapon list). The client
        // filters this down to the player's owned items using its localStorage inventory. This GET serves the
        // INTRINSIC (no-inventory) catalog and stays as a fallback; the page POSTs its inventory to ?handler=Catalog
        // (OnPostCatalogAsync) to get the inventory-aware catalog whose per-item passives include customization
        // R-abilities at the player's owned OB/level (matching what ScoreFixedTeam credits).
        public JsonResult OnGetCatalog()
        {
            return new JsonResult(_analyzer.BuildInteractiveTeamBuilderCatalog());
        }

        // Inventory-aware catalog. Payload: { inventory: <player-inventory-state-v1 JSON string> }. Mirrors
        // OnPostScoreAsync (case-insensitive deserialization + antiforgery). Returns the catalog whose OWNED weapons'
        // Passives are resolved from their owned snapshot through the SAME engine path the scorer uses, so the picker
        // modal and R-ability filter show the customization-derived R-abilities the scorer credits. Falls back to the
        // intrinsic catalog when the inventory is missing/unreadable (BuildInteractiveTeamBuilderCatalog(string) does).
        public async Task<IActionResult> OnPostCatalogAsync()
        {
            CatalogPayload? payload;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<CatalogPayload>(Request.Body, ScoreRequestOptions);
            }
            catch
            {
                return BadRequest(new { error = "Invalid catalog payload." });
            }

            var inventoryJson = payload?.Inventory ?? string.Empty;
            return new JsonResult(_analyzer.BuildInteractiveTeamBuilderCatalog(inventoryJson));
        }

        public sealed class CatalogPayload
        {
            // The raw localStorage 'player-inventory-state-v1' JSON string the client sends (same as ScorePayload.Inventory).
            public string? Inventory { get; set; }
        }

        // Score the posted fixed team. Payload: { inventory: <player-inventory-state-v1 JSON string>, team: InteractiveTeamSpec }.
        public async Task<IActionResult> OnPostScoreAsync()
        {
            ScorePayload? payload;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<ScorePayload>(Request.Body, ScoreRequestOptions);
            }
            catch
            {
                return BadRequest(new { error = "Invalid score payload." });
            }

            if (payload?.Team == null)
            {
                return BadRequest(new { error = "Missing team specification." });
            }

            var inventoryJson = payload.Inventory ?? string.Empty;
            var result = _analyzer.ScoreFixedTeam(inventoryJson, payload.Team, payload.CharacterStats);
            return new JsonResult(result);
        }

        public sealed class ScorePayload
        {
            // The raw localStorage 'player-inventory-state-v1' JSON string the client sends.
            public string? Inventory { get; set; }
            public InteractiveTeamSpec? Team { get; set; }
            // The raw localStorage 'player-character-stats-v1' JSON string (base/stream stats + Highwind), if entered.
            public string? CharacterStats { get; set; }
        }
    }
}
