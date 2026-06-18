using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class PlayerPowerAnalyzerV2Model : PageModel
    {
        private readonly PlayerPowerAnalyzerV2Service _playerPowerAnalyzerV2Service;
        private readonly TeamTemplateCatalog _teamTemplateCatalog;
        private readonly AnalysisJobService _jobs;
        private readonly WeaponSearchDataService _weaponSearchDataService;

        public PlayerPowerAnalyzerV2Model(PlayerPowerAnalyzerV2Service playerPowerAnalyzerV2Service, TeamTemplateCatalog teamTemplateCatalog, AnalysisJobService jobs, WeaponSearchDataService weaponSearchDataService)
        {
            _playerPowerAnalyzerV2Service = playerPowerAnalyzerV2Service;
            _teamTemplateCatalog = teamTemplateCatalog;
            _jobs = jobs;
            _weaponSearchDataService = weaponSearchDataService;
        }

        [BindProperty]
        public string LocalInventoryStateJson { get; set; } = string.Empty;

        [BindProperty]
        public Element EnemyWeakness { get; set; } = Element.None;

        [BindProperty]
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;

        [BindProperty]
        public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;

        // UI default = Fast for a quick first result. (The request-model default may differ — it stays Full for
        // deterministic programmatic/test callers; this UI binding is what end users see selected on page load.)
        [BindProperty]
        public PlayerPowerAnalyzerV2SearchMode SearchMode { get; set; } = PlayerPowerAnalyzerV2SearchMode.Fast;

        // "Pro" deeper sub-weapon optimization (DamageModelMarginal). Full-only: only honored when SearchMode==Full
        // (the JS disables+unchecks it in Fast, and OnPostAnalyze gates it as well so a crafted post can't apply it).
        [BindProperty]
        public bool ProSubWeaponOptimization { get; set; }

        [BindProperty]
        public List<string> RequiredCharacters { get; set; } = new();

        [BindProperty]
        public Dictionary<string, bool> EnabledTeamTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Full character roster (from weapon data) for the Required Characters picker. Backend validation still
        // enforces ownership + mutual exclusion, so listing the whole roster here is safe.
        public IReadOnlyList<string> AvailableCharacters { get; private set; } = Array.Empty<string>();

        [BindProperty]
        public List<string> BossImmunityKeys { get; set; } = new();

        [BindProperty]
        public List<string> HardRequiredEffectKeys { get; set; } = new();

        [BindProperty]
        public List<string> SoftPreferredEffectKeys { get; set; } = new();

        public List<TeamTemplate> AvailableTeamTemplates { get; private set; } = new();
        public IReadOnlyList<PlayerPowerAnalyzerV2EffectOption> AvailableEffectOptions { get; private set; } = Array.Empty<PlayerPowerAnalyzerV2EffectOption>();
        public IReadOnlyList<PlayerPowerAnalyzerV2EffectOption> AvailableBossImmunityOptions { get; private set; } = Array.Empty<PlayerPowerAnalyzerV2EffectOption>();
        public IReadOnlyDictionary<string, List<PlayerPowerAnalyzerV2EffectOption>> EffectOptionsByGroup { get; private set; } = new Dictionary<string, List<PlayerPowerAnalyzerV2EffectOption>>();
        public IReadOnlyDictionary<string, List<PlayerPowerAnalyzerV2EffectOption>> BossImmunityOptionsByGroup { get; private set; } = new Dictionary<string, List<PlayerPowerAnalyzerV2EffectOption>>();
        public PlayerPowerAnalyzerV2Result? AnalysisResult { get; private set; }

        // resultJobId is set by the async flow's completion redirect: pull the finished job's result and render
        // the full page server-side (the result is already computed, so this GET is sub-second).
        public void OnGet(string? resultJobId)
        {
            InitializeSelections();

            if (!string.IsNullOrEmpty(resultJobId) && _jobs.Get(resultJobId)?.Result is PlayerPowerAnalyzerV2Result result)
            {
                AnalysisResult = result;
            }
        }

        private PlayerPowerAnalyzerV2Request BuildRequest()
        {
            return new PlayerPowerAnalyzerV2Request
            {
                EnemyWeakness = EnemyWeakness,
                PreferredDamageType = PreferredDamageType,
                TargetScenario = TargetScenario,
                SearchMode = SearchMode,
                // Pro (DamageModelMarginal) is Full-only: in Fast we force Backbone here (and the service also forces
                // it defensively). Otherwise honor the Pro checkbox.
                SubWeaponSelectionStrategy = (SearchMode == PlayerPowerAnalyzerV2SearchMode.Full && ProSubWeaponOptimization)
                    ? PlayerPowerAnalyzerV2SubWeaponSelectionStrategy.DamageModelMarginal
                    : PlayerPowerAnalyzerV2SubWeaponSelectionStrategy.Backbone,
                EnabledTeamTemplates = EnabledTeamTemplates.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList(),
                RequiredCharacters = RequiredCharacters
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList(),
                BossImmunityKeys = BossImmunityKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                HardRequiredEffectKeys = HardRequiredEffectKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SoftPreferredEffectKeys = SoftPreferredEffectKeys.Distinct(StringComparer.OrdinalIgnoreCase).Except(HardRequiredEffectKeys, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        // Synchronous fallback (no-JS). The JS path uses the async job handlers below so production requests
        // stay under Cloudflare's 100s origin-response timeout.
        public IActionResult OnPostAnalyze()
        {
            InitializeSelections();
            AnalysisResult = _playerPowerAnalyzerV2Service.Analyze(LocalInventoryStateJson, BuildRequest());
            return Page();
        }

        // Start an async analysis job; returns immediately with the job id (sub-second request).
        public IActionResult OnPostStartAnalyze()
        {
            var request = BuildRequest();
            var inventory = LocalInventoryStateJson;
            var job = _jobs.Enqueue((serviceProvider, _) =>
            {
                // Resolve a fresh scoped analyzer from the job's own scope (the request scope is gone by now).
                var service = serviceProvider.GetRequiredService<PlayerPowerAnalyzerV2Service>();
                return service.Analyze(inventory, request);
            });

            return new JsonResult(new { jobId = job.Id });
        }

        // Fast poll: current job state + elapsed time. No result payload here (kept tiny).
        public IActionResult OnGetAnalyzeStatus(string id)
        {
            var job = _jobs.Get(id);
            if (job == null)
            {
                return new JsonResult(new { status = "notfound" }) { StatusCode = 404 };
            }

            return new JsonResult(new
            {
                status = job.Status.ToString().ToLowerInvariant(),
                elapsedMs = job.ElapsedMs,
                error = job.Error
            });
        }

        private void InitializeSelections()
        {
            AvailableTeamTemplates = _teamTemplateCatalog.GetAllTemplates();
            foreach (var template in AvailableTeamTemplates)
            {
                if (!EnabledTeamTemplates.ContainsKey(template.Name))
                {
                    EnabledTeamTemplates[template.Name] = template.Enabled;
                }
            }

            AvailableCharacters = _weaponSearchDataService.GetWeapons()
                .Select(item => item.Character)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AvailableEffectOptions = PlayerPowerAnalyzerV2Service.AvailableEffectOptions;
            AvailableBossImmunityOptions = PlayerPowerAnalyzerV2Service.AvailableBossImmunityOptions;
            EffectOptionsByGroup = AvailableEffectOptions
                .GroupBy(option => option.Group, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
            BossImmunityOptionsByGroup = AvailableBossImmunityOptions
                .Where(option => !option.Group.Equals("Legacy Broad Immunities", StringComparison.OrdinalIgnoreCase))
                .GroupBy(option => option.Group, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
