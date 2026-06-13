using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class PlayerPowerAnalyzerV2Model : PageModel
    {
        private readonly PlayerPowerAnalyzerV2Service _playerPowerAnalyzerV2Service;
        private readonly TeamTemplateCatalog _teamTemplateCatalog;

        public PlayerPowerAnalyzerV2Model(PlayerPowerAnalyzerV2Service playerPowerAnalyzerV2Service, TeamTemplateCatalog teamTemplateCatalog)
        {
            _playerPowerAnalyzerV2Service = playerPowerAnalyzerV2Service;
            _teamTemplateCatalog = teamTemplateCatalog;
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
        public Dictionary<string, bool> EnabledTeamTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

        public void OnGet()
        {
            InitializeSelections();
        }

        public IActionResult OnPostAnalyze()
        {
            InitializeSelections();

            var request = new PlayerPowerAnalyzerV2Request
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
                BossImmunityKeys = BossImmunityKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                HardRequiredEffectKeys = HardRequiredEffectKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SoftPreferredEffectKeys = SoftPreferredEffectKeys.Distinct(StringComparer.OrdinalIgnoreCase).Except(HardRequiredEffectKeys, StringComparer.OrdinalIgnoreCase).ToList()
            };

            AnalysisResult = _playerPowerAnalyzerV2Service.Analyze(LocalInventoryStateJson, request);
            return Page();
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
