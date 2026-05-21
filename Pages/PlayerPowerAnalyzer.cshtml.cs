using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

public class PlayerPowerAnalyzerModel : PageModel
{
    private readonly ILogger<PlayerPowerAnalyzerModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly Gb20Analyzer _gb20Analyzer;
    private readonly WeaponCatalog _weaponCatalog;
    private readonly TeamTemplateCatalog _teamTemplateCatalog;
    private readonly SummonCatalog _summonCatalog;
    private readonly EnemyAbilityCatalog _enemyAbilityCatalog;
    private readonly MemoriaCatalog _memoriaCatalog;

    private enum MateriaTier
    {
        Unknown,
        Pot11Plus,
        Pot8To10,
        Pot0To7
    }

    public PlayerPowerAnalyzerModel(
        ILogger<PlayerPowerAnalyzerModel> logger,
        IConfiguration configuration,
        Gb20Analyzer gb20Analyzer,
        WeaponCatalog weaponCatalog,
        TeamTemplateCatalog teamTemplateCatalog,
        SummonCatalog summonCatalog,
        EnemyAbilityCatalog enemyAbilityCatalog,
        MemoriaCatalog memoriaCatalog)
    {
        _logger = logger;
        _configuration = configuration;
        _gb20Analyzer = gb20Analyzer;
        _weaponCatalog = weaponCatalog;
        _teamTemplateCatalog = teamTemplateCatalog;
        _summonCatalog = summonCatalog;
        _enemyAbilityCatalog = enemyAbilityCatalog;
        _memoriaCatalog = memoriaCatalog;
    }

    public WeaponCatalog WeaponCatalog => _weaponCatalog;

    [BindProperty]
    public string GoogleSheetUrl { get; set; } = string.Empty;

    [BindProperty]
    public string PlayerName { get; set; } = string.Empty;

    [BindProperty]
    public Element EnemyWeakness { get; set; } = Element.None;

    [BindProperty]
    public DamageType PreferredDamageType { get; set; } = DamageType.Any;

    [BindProperty]
    public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;

    [BindProperty]
    public Dictionary<string, int> SynergyEffectBonusPercents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [BindProperty]
    public Dictionary<string, bool> EnabledTeamTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<SheetDefinition> AvailableSheets { get; set; } = new();
    public List<string> AvailablePlayers { get; set; } = new();
    public List<TeamTemplate> AvailableTeamTemplates { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public BestTeamResult? SelectedTeam { get; set; }
    public PlayerGearSummary? SelectedPlayerGear { get; set; }

    public void OnGet()
    {
        LoadAvailableSheets();
        LoadAvailableTeamTemplates();
    }

    public async Task<IActionResult> OnPostLoadPlayersAsync()
    {
        LoadAvailableSheets();
        LoadAvailableTeamTemplates();

        if (string.IsNullOrWhiteSpace(GoogleSheetUrl))
        {
            return new JsonResult(new { players = Array.Empty<string>() });
        }

        var accounts = await LoadAccountsFromSelectedSheetAsync(HttpContext.RequestAborted);
        if (accounts == null)
        {
            return new JsonResult(new { players = Array.Empty<string>() });
        }

        var players = BuildAvailablePlayerList(accounts);
        return new JsonResult(new { players });
    }

    public async Task<IActionResult> OnPostAnalyzeAsync()
    {
        LoadAvailableSheets();
        LoadAvailableTeamTemplates();

        if (string.IsNullOrWhiteSpace(GoogleSheetUrl))
        {
            ErrorMessage = "Please select a survey sheet.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(PlayerName))
        {
            ErrorMessage = "Please select a player.";
            return Page();
        }

        var accounts = await LoadAccountsFromSelectedSheetAsync(HttpContext.RequestAborted);
        if (accounts == null || accounts.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(ErrorMessage))
            {
                ErrorMessage = "No survey data was found in the selected sheet.";
            }

            return Page();
        }

        AvailablePlayers = BuildAvailablePlayerList(accounts);
        var selectedAccount = accounts.FirstOrDefault(a => string.Equals(a.InGameName?.Trim(), PlayerName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (selectedAccount == null)
        {
            ErrorMessage = "Selected player was not found in the selected sheet.";
            return Page();
        }

        var enabledTemplateNames = EnabledTeamTemplates
            .Where(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();

        var battleContext = new BattleContext
        {
            EnemyWeakness = EnemyWeakness,
            PreferredDamageType = PreferredDamageType,
            TargetScenario = TargetScenario,
            SynergyEffectBonusPercents = SynergyEffectBonusPercents,
            EnabledTeamTemplates = enabledTemplateNames
        };

        var analyzedTeams = await _gb20Analyzer.AnalyzeAsync(new[] { selectedAccount }, battleContext);
        SelectedTeam = analyzedTeams.FirstOrDefault();
        if (SelectedTeam == null)
        {
            ErrorMessage = "Unable to analyze the selected player.";
            return Page();
        }

        if (selectedAccount.ItemResponsesByColumnName.TryGetValue("Your Guild", out var submittedGuild) && !string.IsNullOrWhiteSpace(submittedGuild))
        {
            SelectedTeam.SubmittedGuild = submittedGuild.Trim();
        }

        if (selectedAccount.ItemResponsesByColumnName.TryGetValue("Battle release day banner?", out var bannerResponse) && !string.IsNullOrWhiteSpace(bannerResponse))
        {
            SelectedTeam.BannerResponse = bannerResponse.Trim();
        }

        SelectedPlayerGear = BuildPlayerGearSummary(selectedAccount, SelectedTeam, battleContext);
        return Page();
    }

    private void LoadAvailableSheets()
    {
        var configuredSheets = _configuration.GetSection("GoogleSheets:SurveySheets")
            .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AvailableSheets = configuredSheets
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Url))
            .Where(s => seenKeys.Add($"{s.Name.Trim()}|{s.Url.Trim()}"))
            .ToList();
    }

    private void LoadAvailableTeamTemplates()
    {
        AvailableTeamTemplates = _teamTemplateCatalog.GetAllTemplates();
        foreach (var template in AvailableTeamTemplates)
        {
            if (!EnabledTeamTemplates.ContainsKey(template.Name))
            {
                EnabledTeamTemplates[template.Name] = template.Enabled;
            }
        }
    }

    private async Task<List<AccountRow>?> LoadAccountsFromSelectedSheetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(GoogleSheetUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to download Google Sheet: {response.StatusCode}";
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var ingestionResult = await _gb20Analyzer.ReadAccountsAsync(stream);
            return ingestionResult.Accounts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load survey sheet for PlayerPowerAnalyzer");
            ErrorMessage = $"Failed to parse selected sheet: {ex.Message}";
            return null;
        }
    }

    private static List<string> BuildAvailablePlayerList(IReadOnlyList<AccountRow> accounts)
    {
        return accounts
            .Select(a => a.InGameName?.Trim() ?? string.Empty)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private PlayerGearSummary BuildPlayerGearSummary(AccountRow account, BestTeamResult team, BattleContext context)
    {
        var summary = new PlayerGearSummary
        {
            InGameName = team.InGameName,
            DiscordName = team.DiscordName,
            Score = team.Score,
            BestTeamCharacters = team.Characters?.ToList() ?? new List<string>(),
            MissingCatalogItems = team.Breakdown?.MissingCatalogItems?.ToList() ?? new List<MissingCatalogItemBreakdown>()
        };

        var weaponsByCharacter = new Dictionary<string, List<PlayerGearWeaponEntry>>(StringComparer.OrdinalIgnoreCase);
        var costumesByCharacter = new Dictionary<string, List<PlayerGearCostumeEntry>>(StringComparer.OrdinalIgnoreCase);
        var memoriaEntries = new List<PlayerGearLeveledEntry>();
        var summonEntries = new List<PlayerGearLeveledEntry>();
        var enemyAbilityEntries = new List<PlayerGearLeveledEntry>();
        var materiaByName = new Dictionary<string, PlayerGearMateriaEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in account.ItemResponsesByColumnName)
        {
            var columnName = kvp.Key;
            var rawValue = kvp.Value;

            if (_weaponCatalog.TryGetWeapon(columnName, out var weaponInfo))
            {
                var ob = ParseWeaponOverboost(rawValue);
                if (ob.HasValue)
                {
                    if (!weaponsByCharacter.TryGetValue(weaponInfo.Character, out var weaponList))
                    {
                        weaponList = new List<PlayerGearWeaponEntry>();
                        weaponsByCharacter[weaponInfo.Character] = weaponList;
                    }

                    double? abilityPotPercent = null;
                    if (weaponInfo.PotPercentByOb.TryGetValue(ob.Value, out var potAtOb))
                    {
                        abilityPotPercent = potAtOb;
                    }
                    else if (ob.Value == 10)
                    {
                        abilityPotPercent = weaponInfo.AbilityPotPercentAtOb10;
                    }

                    string? synergyReason = null;
                    if (SynergyDetection.CountSynergyMatches(weaponInfo, context) > 0)
                    {
                        synergyReason = SynergyDetection.DescribeSynergyMatches(weaponInfo, context);
                    }

                    weaponList.Add(new PlayerGearWeaponEntry
                    {
                        WeaponName = weaponInfo.Name,
                        OverboostLevel = ob.Value,
                        IsUltimate = weaponInfo.IsUltimate,
                        AbilityPotPercent = abilityPotPercent,
                        SynergyReason = synergyReason
                    });
                }

                continue;
            }

            if (_weaponCatalog.TryGetCostume(columnName, out var costumeInfo) && IsOwnedValue(rawValue))
            {
                if (!costumesByCharacter.TryGetValue(costumeInfo.Character, out var costumeList))
                {
                    costumeList = new List<PlayerGearCostumeEntry>();
                    costumesByCharacter[costumeInfo.Character] = costumeList;
                }

                costumeList.Add(new PlayerGearCostumeEntry { CostumeName = costumeInfo.Name });
                continue;
            }

            if (_summonCatalog.TryGetSummon(columnName, out var summonDef))
            {
                var level = ParseLevel(rawValue);
                if (level.HasValue && level.Value > 0)
                {
                    summonEntries.Add(new PlayerGearLeveledEntry
                    {
                        Name = summonDef.Name,
                        Level = level.Value,
                        MaxLevel = Math.Max(1, summonDef.MaxLevel)
                    });
                }

                continue;
            }

            if (_enemyAbilityCatalog.TryGetEnemyAbility(columnName, out var enemyAbilityDef))
            {
                var level = ParseLevel(rawValue);
                if (level.HasValue && level.Value > 0)
                {
                    enemyAbilityEntries.Add(new PlayerGearLeveledEntry
                    {
                        Name = enemyAbilityDef.Name,
                        Level = level.Value,
                        MaxLevel = Math.Max(1, enemyAbilityDef.MaxLevel)
                    });
                }

                continue;
            }

            if (_memoriaCatalog.TryGetMemoria(columnName, out var memoriaDef))
            {
                var level = ParseLevel(rawValue);
                if (level.HasValue && level.Value > 0)
                {
                    memoriaEntries.Add(new PlayerGearLeveledEntry
                    {
                        Name = memoriaDef.Name,
                        Level = level.Value,
                        MaxLevel = Math.Max(1, memoriaDef.MaxLevel)
                    });
                }

                continue;
            }

            if (TryParseMateriaColumn(columnName, out var materiaName, out var tier))
            {
                var count = ParseCountValue(rawValue);
                if (count <= 0)
                {
                    continue;
                }

                if (!materiaByName.TryGetValue(materiaName, out var materiaEntry))
                {
                    materiaEntry = new PlayerGearMateriaEntry { MateriaName = materiaName };
                    materiaByName[materiaName] = materiaEntry;
                }

                switch (tier)
                {
                    case MateriaTier.Pot11Plus:
                        materiaEntry.CountPot11Plus += count;
                        break;
                    case MateriaTier.Pot8To10:
                        materiaEntry.CountPot8To10 += count;
                        break;
                    case MateriaTier.Pot0To7:
                        materiaEntry.CountPot0To7 += count;
                        break;
                }
            }
        }

        summary.WeaponsByCharacter = weaponsByCharacter
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
                    .OrderByDescending(w => w.OverboostLevel)
                    .ThenBy(w => w.WeaponName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        summary.CostumesByCharacter = costumesByCharacter
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.OrderBy(c => c.CostumeName, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        summary.Summons = summonEntries
            .OrderByDescending(s => s.Level)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        summary.EnemyAbilities = enemyAbilityEntries
            .OrderByDescending(e => e.Level)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        summary.Memoria = memoriaEntries
            .OrderByDescending(m => m.Level)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        summary.Materia = materiaByName.Values
            .OrderByDescending(m => m.TotalCount)
            .ThenBy(m => m.MateriaName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return summary;
    }

    private static bool IsOwnedValue(string rawValue)
    {
        return rawValue.Equals("Own", StringComparison.OrdinalIgnoreCase)
               || rawValue.Equals("Owned", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseLevel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (raw.Equals("Do Not Own", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (raw.StartsWith("Level", StringComparison.OrdinalIgnoreCase))
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var parsedLevel))
            {
                return parsedLevel;
            }
        }

        return int.TryParse(raw, out var numericLevel) ? numericLevel : null;
    }

    private static bool TryParseMateriaColumn(string columnName, out string materiaName, out MateriaTier tier)
    {
        materiaName = string.Empty;
        tier = MateriaTier.Unknown;

        if (string.IsNullOrWhiteSpace(columnName) || !columnName.EndsWith(" Owned", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var materiaIndex = columnName.IndexOf(" Materia (", StringComparison.OrdinalIgnoreCase);
        if (materiaIndex < 0)
        {
            return false;
        }

        var closeParenIndex = columnName.IndexOf(')', materiaIndex);
        if (closeParenIndex < 0)
        {
            return false;
        }

        materiaName = columnName.Substring(0, materiaIndex).Trim();
        var tierText = columnName.Substring(materiaIndex + " Materia (".Length, closeParenIndex - (materiaIndex + " Materia (".Length)).Trim();

        if (tierText.Contains("11%", StringComparison.OrdinalIgnoreCase) || tierText.Contains("or higher", StringComparison.OrdinalIgnoreCase))
        {
            tier = MateriaTier.Pot11Plus;
            return !string.IsNullOrWhiteSpace(materiaName);
        }

        if (tierText.Contains("8-10%", StringComparison.OrdinalIgnoreCase) || tierText.Contains("8–10%", StringComparison.OrdinalIgnoreCase))
        {
            tier = MateriaTier.Pot8To10;
            return !string.IsNullOrWhiteSpace(materiaName);
        }

        if (tierText.Contains("0-7%", StringComparison.OrdinalIgnoreCase) || tierText.Contains("0–7%", StringComparison.OrdinalIgnoreCase))
        {
            tier = MateriaTier.Pot0To7;
            return !string.IsNullOrWhiteSpace(materiaName);
        }

        return false;
    }

    private static int ParseCountValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        raw = raw.Trim();
        if (raw.EndsWith("+", StringComparison.OrdinalIgnoreCase))
        {
            var num = raw[..^1];
            if (int.TryParse(num, out var plusVal))
            {
                return Math.Max(0, plusVal);
            }
        }

        if (int.TryParse(raw, out var val))
        {
            return Math.Max(0, val);
        }

        return 0;
    }

    private static int? ParseWeaponOverboost(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (raw.Equals("Do Not Own", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (raw.Equals("Own", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("Owned", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("5 Star", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (raw.StartsWith("OB", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(raw[2..], out var ob)
            && ob >= 0
            && ob <= 10)
        {
            return ob;
        }

        return null;
    }
}
