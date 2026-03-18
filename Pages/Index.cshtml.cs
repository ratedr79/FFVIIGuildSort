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

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly Gb20Analyzer _gb20Analyzer;
    private readonly GuildAssigner _guildAssigner;
    private readonly WeaponCatalog _weaponCatalog;
    private readonly TeamTemplateCatalog _teamTemplateCatalog;

    public IndexModel(ILogger<IndexModel> logger, Gb20Analyzer gb20Analyzer, GuildAssigner guildAssigner, WeaponCatalog weaponCatalog, TeamTemplateCatalog teamTemplateCatalog)
    {
        _logger = logger;
        _gb20Analyzer = gb20Analyzer;
        _guildAssigner = guildAssigner;
        _weaponCatalog = weaponCatalog;
        _teamTemplateCatalog = teamTemplateCatalog;
    }

    public WeaponCatalog WeaponCatalog => _weaponCatalog;

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

    public void OnGet()
    {
        // Load available templates and set all as enabled by default
        AvailableTeamTemplates = _teamTemplateCatalog.GetAllTemplates();
        foreach (var template in AvailableTeamTemplates)
        {
            EnabledTeamTemplates[template.Name] = template.Enabled;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (UploadedFile == null || UploadedFile.Length == 0)
        {
            ErrorMessage = "Please select a CSV file to upload.";
            return Page();
        }

        if (!Path.GetExtension(UploadedFile.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Please upload a valid CSV file.";
            return Page();
        }

        try
        {
            using var stream = UploadedFile.OpenReadStream();
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

            RankedTeams = await _gb20Analyzer.AnalyzeAsync(accounts, new BattleContext
            {
                EnemyWeakness = EnemyWeakness,
                PreferredDamageType = PreferredDamageType,
                TargetScenario = TargetScenario,
                SynergyEffectBonusPercents = SynergyEffectBonusPercents,
                EnabledTeamTemplates = enabledTemplateNames
            });

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
        if (UploadedFile == null || UploadedFile.Length == 0)
        {
            ErrorMessage = "Please select a CSV file to upload.";
            return Page();
        }

        if (!Path.GetExtension(UploadedFile.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Please upload a valid CSV file.";
            return Page();
        }

        try
        {
            using var stream = UploadedFile.OpenReadStream();
            var ingestionResult = await _gb20Analyzer.ReadAccountsAsync(stream);
            var accounts = ingestionResult.Accounts;
            
            var rankedTeams = await _gb20Analyzer.AnalyzeAsync(accounts, new BattleContext
            {
                EnemyWeakness = EnemyWeakness,
                PreferredDamageType = PreferredDamageType,
                TargetScenario = TargetScenario
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

            var sb = new StringBuilder();
            sb.AppendLine("Guild,In-Game Name,Reason,Banner Response");

            foreach (var a in assignmentResult.Assignments
                         .OrderBy(x => x.Guild)
                         .ThenBy(x => x.Player, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(a.Guild);
                sb.Append(',');
                sb.Append(EscapeCsv(a.Player));
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
}
