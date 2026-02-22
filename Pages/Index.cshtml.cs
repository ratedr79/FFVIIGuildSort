using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using FFVIIEverCrisisAnalyzer.Services;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly Gb20Analyzer _gb20Analyzer;
    private readonly GuildAssigner _guildAssigner;

    public IndexModel(ILogger<IndexModel> logger, Gb20Analyzer gb20Analyzer, GuildAssigner guildAssigner)
    {
        _logger = logger;
        _gb20Analyzer = gb20Analyzer;
        _guildAssigner = guildAssigner;
    }

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

    public string? ErrorMessage { get; set; }
    public List<BestTeamResult> RankedTeams { get; set; } = new();
    public List<PlayerGuildAssignment> GuildAssignments { get; set; } = new();
    public List<string> GuildWarnings { get; set; } = new();

    public void OnGet()
    {
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
            RankedTeams = await _gb20Analyzer.AnalyzeAsync(stream, new BattleContext
            {
                EnemyWeakness = EnemyWeakness,
                PreferredDamageType = PreferredDamageType,
                TargetScenario = TargetScenario
            });

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
            var rankedTeams = await _gb20Analyzer.AnalyzeAsync(stream, new BattleContext
            {
                EnemyWeakness = EnemyWeakness,
                PreferredDamageType = PreferredDamageType,
                TargetScenario = TargetScenario
            });

            var rules = _guildAssigner.LoadRulesOrDefault();
            var assignmentResult = _guildAssigner.AssignGuilds(rankedTeams, rules);

            var sb = new StringBuilder();
            sb.AppendLine("Guild,In-Game Name,Reason");

            foreach (var a in assignmentResult.Assignments
                         .OrderBy(x => x.Guild)
                         .ThenBy(x => x.Player, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(a.Guild);
                sb.Append(',');
                sb.Append(EscapeCsv(a.Player));
                sb.Append(',');
                sb.AppendLine(EscapeCsv(a.Reason ?? string.Empty));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
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
