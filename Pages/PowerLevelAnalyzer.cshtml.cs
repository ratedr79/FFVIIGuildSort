using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using FFVIIEverCrisisAnalyzer.Services;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Pages;

public class PlayerSubmissionInfo
{
    public string InGameName { get; set; } = string.Empty;
    public string? DiscordName { get; set; }
    public string Guild { get; set; } = string.Empty;
}

public sealed class PlayerGearWeaponEntry
{
    public string WeaponName { get; set; } = string.Empty;
    public int OverboostLevel { get; set; }
    public bool IsUltimate { get; set; }
    public double? AbilityPotPercent { get; set; }
    public string? SynergyReason { get; set; }
}

public sealed class PlayerGearCostumeEntry
{
    public string CostumeName { get; set; } = string.Empty;
}

public sealed class PlayerGearLeveledEntry
{
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public int MaxLevel { get; set; }
}

public sealed class PlayerGearMateriaEntry
{
    public string MateriaName { get; set; } = string.Empty;
    public int CountPot11Plus { get; set; }
    public int CountPot8To10 { get; set; }
    public int CountPot0To7 { get; set; }
    public int TotalCount => CountPot11Plus + CountPot8To10 + CountPot0To7;
}

public sealed class PlayerGearSummary
{
    public string InGameName { get; set; } = string.Empty;
    public string? DiscordName { get; set; }
    public double Score { get; set; }
    public List<string> BestTeamCharacters { get; set; } = new();
    public Dictionary<string, List<PlayerGearWeaponEntry>> WeaponsByCharacter { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<PlayerGearCostumeEntry>> CostumesByCharacter { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PlayerGearLeveledEntry> Summons { get; set; } = new();
    public List<PlayerGearLeveledEntry> EnemyAbilities { get; set; } = new();
    public List<PlayerGearLeveledEntry> Memoria { get; set; } = new();
    public List<PlayerGearMateriaEntry> Materia { get; set; } = new();
    public List<MissingCatalogItemBreakdown> MissingCatalogItems { get; set; } = new();
}

public class PowerLevelAnalyzerModel : PageModel
{
    private readonly ILogger<PowerLevelAnalyzerModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly Gb20Analyzer _gb20Analyzer;
    private readonly GuildAssigner _guildAssigner;
    private readonly WeaponCatalog _weaponCatalog;
    private readonly TeamTemplateCatalog _teamTemplateCatalog;
    private readonly SummonCatalog _summonCatalog;
    private readonly EnemyAbilityCatalog _enemyAbilityCatalog;
    private readonly MemoriaCatalog _memoriaCatalog;

    public PowerLevelAnalyzerModel(ILogger<PowerLevelAnalyzerModel> logger, IConfiguration configuration, Gb20Analyzer gb20Analyzer, GuildAssigner guildAssigner, WeaponCatalog weaponCatalog, TeamTemplateCatalog teamTemplateCatalog, SummonCatalog summonCatalog, EnemyAbilityCatalog enemyAbilityCatalog, MemoriaCatalog memoriaCatalog)
    {
        _logger = logger;
        _configuration = configuration;
        _gb20Analyzer = gb20Analyzer;
        _guildAssigner = guildAssigner;
        _weaponCatalog = weaponCatalog;
        _teamTemplateCatalog = teamTemplateCatalog;
        _summonCatalog = summonCatalog;
        _enemyAbilityCatalog = enemyAbilityCatalog;
        _memoriaCatalog = memoriaCatalog;
    }

    public WeaponCatalog WeaponCatalog => _weaponCatalog;

    [BindProperty]
    public string? GoogleSheetUrl { get; set; }

    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    [BindProperty]
    public Element EnemyWeakness { get; set; } = Element.None;

    [BindProperty]
    public DamageType PreferredDamageType { get; set; } = DamageType.Any;

    [BindProperty]
    public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;

    [BindProperty]
    public bool ShowDebug { get; set; } = false;

    [BindProperty]
    public Dictionary<string, int> SynergyEffectBonusPercents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [BindProperty]
    public Dictionary<string, bool> EnabledTeamTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<TeamTemplate> AvailableTeamTemplates { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public List<BestTeamResult> RankedTeams { get; set; } = new();
    public List<PlayerGuildAssignment> GuildAssignments { get; set; } = new();
    public List<string> GuildWarnings { get; set; } = new();
    public Dictionary<int, string> GuildTimeZoneSummaries { get; set; } = new();
    public Dictionary<string, int> GuildSubmissionCounts { get; set; } = new();
    public int TotalDistinctPlayers { get; set; }
    public List<string> DuplicateSubmitters { get; set; } = new();
    public List<PlayerSubmissionInfo> AllPlayerSubmissions { get; set; } = new();
    public List<SheetDefinition> AvailableSheets { get; set; } = new();
    public Dictionary<string, PlayerGearSummary> PlayerGearByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void OnGet()
    {
        LoadAvailableSheets();
        // Load available templates and set all as enabled by default
        AvailableTeamTemplates = _teamTemplateCatalog.GetAllTemplates();
        foreach (var template in AvailableTeamTemplates)
        {
            EnabledTeamTemplates[template.Name] = template.Enabled;
        }
    }

    private void LoadAvailableSheets()
    {
        var configuredSheets = _configuration.GetSection("GoogleSheets:SurveySheets")
            .Get<List<SheetDefinition>>() ?? new List<SheetDefinition>();

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AvailableSheets = configuredSheets
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Url))
            .Where(s =>
            {
                var key = $"{s.Name.Trim()}|{s.Url.Trim()}";
                return seenKeys.Add(key);
            })
            .ToList();
    }

    private async Task<Stream?> GetCsvStreamAsync()
    {
        if (!string.IsNullOrWhiteSpace(GoogleSheetUrl))
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(GoogleSheetUrl);
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to download Google Sheet: {response.StatusCode}";
                return null;
            }
            var memStream = new MemoryStream();
            await response.Content.CopyToAsync(memStream);
            memStream.Position = 0;
            return memStream;
        }

        if (UploadedFile != null && UploadedFile.Length > 0)
        {
            if (!Path.GetExtension(UploadedFile.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "Please upload a valid CSV file.";
                return null;
            }
            return UploadedFile.OpenReadStream();
        }

        ErrorMessage = "Please select a survey sheet or upload a CSV file.";
        return null;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        LoadAvailableSheets();
        using var stream = await GetCsvStreamAsync();
        if (stream == null)
        {
            return Page();
        }

        try
        {
            var ingestionResult = await _gb20Analyzer.ReadAccountsAsync(stream);
            var accounts = ingestionResult.Accounts;
            DuplicateSubmitters = ingestionResult.DuplicateSubmitters;

            // Build guild submission counts from deduplicated accounts
            GuildSubmissionCounts = accounts
                .Where(a => a.ItemResponsesByColumnName.ContainsKey("Your Guild"))
                .GroupBy(a => a.ItemResponsesByColumnName["Your Guild"], StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // Calculate total distinct players
            TotalDistinctPlayers = accounts.Count;

            // Build list of all player submissions sorted by guild and in-game name
            AllPlayerSubmissions = accounts
                .Select(a => new PlayerSubmissionInfo
                {
                    InGameName = a.InGameName?.Trim() ?? string.Empty,
                    DiscordName = a.ItemResponsesByColumnName.TryGetValue("Discord Name (If different)", out var discord) && !string.IsNullOrWhiteSpace(discord) 
                        ? discord.Trim() 
                        : null,
                    Guild = a.ItemResponsesByColumnName.TryGetValue("Your Guild", out var guild) && !string.IsNullOrWhiteSpace(guild)
                        ? guild.Trim()
                        : "Unknown"
                })
                .OrderBy(p => p.Guild, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.InGameName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Build list of enabled team templates
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

            RankedTeams = await _gb20Analyzer.AnalyzeAsync(accounts, battleContext);
            PlayerGearByName = BuildPlayerGearSummaries(accounts, RankedTeams, battleContext);

            // Reload templates for display
            AvailableTeamTemplates = _teamTemplateCatalog.GetAllTemplates();

            var rules = _guildAssigner.LoadRulesOrDefault();
            var assignmentResult = _guildAssigner.AssignGuilds(RankedTeams, rules);
            GuildAssignments = assignmentResult.Assignments;
            GuildWarnings = assignmentResult.Warnings;

            var guildByPlayer = GuildAssignments
                .GroupBy(a => a.Player, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Guild, StringComparer.OrdinalIgnoreCase);

            // Ensure configured players appear in the table even if not in the CSV.
            foreach (var a in GuildAssignments)
            {
                if (!RankedTeams.Any(t => t.InGameName.Equals(a.Player, StringComparison.OrdinalIgnoreCase)))
                {
                    RankedTeams.Add(new BestTeamResult
                    {
                        InGameName = a.Player,
                        Score = 0,
                        Characters = new List<string>(),
                        Breakdown = null
                    });
                }
            }

            foreach (var t in RankedTeams)
            {
                if (guildByPlayer.TryGetValue(t.InGameName, out var g))
                {
                    t.GuildNumber = g;
                }
            }

            var tzByPlayer = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.InGameName))
                .GroupBy(a => a.InGameName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().ItemResponsesByColumnName.TryGetValue("Your Time Zone", out var tz) ? tz : string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase);

            var bannerByPlayer = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.InGameName))
                .GroupBy(a => a.InGameName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().ItemResponsesByColumnName.TryGetValue("Battle release day banner?", out var banner) ? banner : string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase);

            var submittedGuildByPlayer = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.InGameName))
                .GroupBy(a => a.InGameName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().ItemResponsesByColumnName.TryGetValue("Your Guild", out var guild) ? guild : string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation($"Banner responses found: {bannerByPlayer.Count(kvp => !string.IsNullOrWhiteSpace(kvp.Value))}");
            foreach (var kvp in bannerByPlayer.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)))
            {
                _logger.LogInformation($"Player: {kvp.Key}, Banner Response: {kvp.Value}");
            }

            foreach (var t in RankedTeams)
            {
                if (bannerByPlayer.TryGetValue(t.InGameName, out var bannerResponse) && !string.IsNullOrWhiteSpace(bannerResponse))
                {
                    t.BannerResponse = bannerResponse;
                    _logger.LogInformation($"Set banner response for {t.InGameName}: {bannerResponse}");
                }
                
                if (submittedGuildByPlayer.TryGetValue(t.InGameName, out var submittedGuild) && !string.IsNullOrWhiteSpace(submittedGuild))
                {
                    t.SubmittedGuild = submittedGuild;
                }
            }

            GuildTimeZoneSummaries = GuildAssignments
                .GroupBy(a => a.Guild)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join(
                        ", ",
                        g
                            .Select(a => tzByPlayer.TryGetValue(a.Player, out var tz) && !string.IsNullOrWhiteSpace(tz) ? tz : "N/A")
                            .GroupBy(tz => tz, StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(x => x.Count())
                            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(x => $"{x.Key} ({x.Count()})")
                    ));

            RankedTeams = RankedTeams
                .OrderByDescending(t => t.Score)
                .ThenBy(t => t.InGameName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            _logger.LogInformation($"Successfully processed {RankedTeams.Count} accounts from gb20 CSV file.");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error processing file: {ex.Message}";
            _logger.LogError(ex, "Error processing CSV file");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostExportGuildsAsync()
    {
        LoadAvailableSheets();
        using var stream = await GetCsvStreamAsync();
        if (stream == null)
        {
            return Page();
        }

        try
        {
            var ingestionResult = await _gb20Analyzer.ReadAccountsAsync(stream);
            var accounts = ingestionResult.Accounts;
            
            // Build list of enabled team templates
            var enabledTemplateNames = EnabledTeamTemplates
                .Where(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();
            
            var rankedTeams = await _gb20Analyzer.AnalyzeAsync(accounts, new BattleContext
            {
                EnemyWeakness = EnemyWeakness,
                PreferredDamageType = PreferredDamageType,
                TargetScenario = TargetScenario,
                SynergyEffectBonusPercents = SynergyEffectBonusPercents,
                EnabledTeamTemplates = enabledTemplateNames
            });

            var rules = _guildAssigner.LoadRulesOrDefault();
            var assignmentResult = _guildAssigner.AssignGuilds(rankedTeams, rules);

            // Build banner response lookup
            var bannerByPlayer = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.InGameName))
                .GroupBy(a => a.InGameName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().ItemResponsesByColumnName.TryGetValue("Battle release day banner?", out var banner) ? banner : string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase);

            // Build Discord name lookup
            var discordByPlayer = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.InGameName))
                .GroupBy(a => a.InGameName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().ItemResponsesByColumnName.TryGetValue("Discord Name (If different)", out var discord) ? discord : string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase);

            // Build submitted guild lookup
            var submittedGuildByPlayer = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.InGameName))
                .GroupBy(a => a.InGameName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().ItemResponsesByColumnName.TryGetValue("Your Guild", out var guild) ? guild : string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase);

            // Build score lookup from ranked teams
            var scoreByPlayer = rankedTeams
                .ToDictionary(
                    t => t.InGameName,
                    t => t.Score,
                    StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.AppendLine("Guild,In-Game Name,Discord Name,Submitted Guild,Score,Reason,Banner Response");

            foreach (var a in assignmentResult.Assignments
                         .OrderBy(x => x.Guild)
                         .ThenByDescending(x => scoreByPlayer.TryGetValue(x.Player, out var s) ? s : 0)
                         .ThenBy(x => x.Player, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(a.Guild);
                sb.Append(',');
                sb.Append(EscapeCsv(a.Player));
                sb.Append(',');
                var discordName = discordByPlayer.TryGetValue(a.Player, out var dn) ? dn : string.Empty;
                sb.Append(EscapeCsv(discordName));
                sb.Append(',');
                var submittedGuild = submittedGuildByPlayer.TryGetValue(a.Player, out var sg) ? sg : string.Empty;
                sb.Append(EscapeCsv(submittedGuild));
                sb.Append(',');
                var score = scoreByPlayer.TryGetValue(a.Player, out var s) ? s.ToString("F0") : "0";
                sb.Append(score);
                sb.Append(',');
                sb.Append(EscapeCsv(a.Reason ?? string.Empty));
                sb.Append(',');
                var bannerResponse = bannerByPlayer.TryGetValue(a.Player, out var br) ? br : string.Empty;
                sb.AppendLine(EscapeCsv(bannerResponse));
            }

            // Use UTF-8 with BOM for proper Japanese character display in Excel
            var utf8WithBom = new UTF8Encoding(true);
            using var memoryStream = new MemoryStream();
            using (var writer = new StreamWriter(memoryStream, utf8WithBom, leaveOpen: true))
            {
                writer.Write(sb.ToString());
            }
            var bytes = memoryStream.ToArray();
            var fileName = $"guild-assignments_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error processing file: {ex.Message}";
            _logger.LogError(ex, "Error exporting guild assignments");
            return Page();
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        if (value.Contains(',') || value.Contains('\n') || value.Contains('\r') || value.Contains('"'))
        {
            return $"\"{value}\"";
        }

        return value;
    }

    private Dictionary<string, PlayerGearSummary> BuildPlayerGearSummaries(IReadOnlyList<AccountRow> accounts, IReadOnlyList<BestTeamResult> teams, BattleContext context)
    {
        var summaries = new Dictionary<string, PlayerGearSummary>(StringComparer.OrdinalIgnoreCase);
        var accountByName = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.InGameName))
            .GroupBy(a => a.InGameName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            if (!accountByName.TryGetValue(team.InGameName, out var account))
            {
                continue;
            }

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
                        if (SynergyDetection.CountSynergyMatches(weaponInfo.EffectTextBlob, context) > 0)
                        {
                            synergyReason = SynergyDetection.DescribeSynergyMatches(weaponInfo.EffectTextBlob, context);
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

            summaries[team.InGameName] = summary;
        }

        return summaries;
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

        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        if (!columnName.EndsWith(" Owned", StringComparison.OrdinalIgnoreCase))
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

        if (raw.Equals("Own", StringComparison.OrdinalIgnoreCase) || raw.Equals("Owned", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (raw.Equals("5 Star", StringComparison.OrdinalIgnoreCase))
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
