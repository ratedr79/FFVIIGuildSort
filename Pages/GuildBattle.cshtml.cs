using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FFVIIEverCrisisAnalyzer.Services;
using FFVIIEverCrisisAnalyzer.Models;
using System.Text;
using System;
using System.Linq;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Pages
{
    public class GuildBattleModel : PageModel
    {
        private readonly ILogger<GuildBattleModel> _logger;
        private readonly GuildBattleParser _parser = new();
        private readonly GuildBattleAssignmentEngine _engine = new();

        public GuildBattleModel(ILogger<GuildBattleModel> logger)
        {
            _logger = logger;
        }

        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        [BindProperty]
        public int CurrentDay { get; set; } = 1; // 1..3

        [BindProperty]
        public double MarginOfErrorPercent { get; set; } = 7.5; // default

        public GuildBattleParseResult? ParseResult { get; set; }
        public TodayState? Today { get; set; }
        public BattlePlanSummary? BattlePlan { get; set; }
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAnalyzeAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                ErrorMessage = "Please select a CSV file to upload.";
                return Page();
            }

            try
            {
                using var stream = UploadedFile.OpenReadStream();
                ParseResult = await _parser.ParseAsync(stream);

                var players = ParseResult?.Players;
                if (players == null || players.Count == 0)
                {
                    ErrorMessage = "No player data found in CSV.";
                    return Page();
                }

                CurrentDay = Math.Clamp(CurrentDay, 1, 3);

                var today = new TodayState
                {
                    CurrentDay = CurrentDay,
                    RemainingHpByStage = Enum.GetValues<StageId>().ToDictionary(s => s, s => 100.0),
                    KillsToday = Enum.GetValues<StageId>().ToDictionary(s => s, s => 0)
                };

                // Count attempts today - simplified
                int attemptsToday = 0;
                int totalS5KillsAllDays = 0;
                
                foreach (var player in players)
                {
                    if (player.Attempts == null) continue;
                    foreach (var attempt in player.Attempts)
                    {
                        if (attempt.Day == CurrentDay) attemptsToday++;
                        if (attempt.Stage == StageId.S5 && attempt.Killed) totalS5KillsAllDays++;
                    }
                }
                
                today.RemainingHits = Math.Max(0, 90 - attemptsToday);
                today.IsStage6Unlocked = totalS5KillsAllDays >= 5;

                HashSet<StageId> unlockedStages = new(Enum.GetValues<StageId>().Where(s => s != StageId.S6));
                if (today.IsStage6Unlocked) unlockedStages.Add(StageId.S6);

                foreach (var s in Enum.GetValues<StageId>())
                {
                    today.RemainingHpByStage[s] = unlockedStages.Contains(s) ? 100.0 : 0.0;
                }

                // Track kills from previous days
                var killsPreviousDays = new Dictionary<StageId, int>();
                foreach (var s in Enum.GetValues<StageId>())
                {
                    killsPreviousDays[s] = 0;
                }

                // Process ALL attempts in order (previous days first, then today)
                var allAttempts = new List<AttemptLog>();
                foreach (var player in players)
                {
                    if (player.Attempts == null) continue;
                    foreach (var attempt in player.Attempts)
                    {
                        allAttempts.Add(attempt);
                    }
                }
                allAttempts.Sort((a, b) => 
                {
                    var dayCompare = a.Day.CompareTo(b.Day);
                    if (dayCompare != 0) return dayCompare;
                    return a.RowIndex.CompareTo(b.RowIndex);
                });

                foreach (var a in allAttempts)
                {
                    if (!unlockedStages.Contains(a.Stage)) continue;
                    
                    bool isToday = a.Day == CurrentDay;
                    
                    if (a.Killed)
                    {
                        // Explicit kill from CSV - reset HP and count the kill
                        today.RemainingHpByStage[a.Stage] = 0.0;
                        
                        if (isToday)
                        {
                            today.KillsToday[a.Stage] += 1;
                        }
                        else
                        {
                            killsPreviousDays[a.Stage] += 1;
                        }
                        
                        if (a.Stage == StageId.S5)
                        {
                            totalS5KillsAllDays += 1;
                            if (!today.IsStage6Unlocked && totalS5KillsAllDays >= 5)
                            {
                                today.IsStage6Unlocked = true;
                                unlockedStages.Add(StageId.S6);
                                if (!today.RemainingHpByStage.ContainsKey(StageId.S6))
                                    today.RemainingHpByStage[StageId.S6] = 100.0;
                            }
                        }
                    }
                    else
                    {
                        // Apply damage for HP tracking, but don't count as kill
                        // We don't know true attack order (players are alphabetical) and there may be rounding errors
                        var remaining = today.RemainingHpByStage[a.Stage];
                        var after = Math.Max(0.0, remaining - Math.Max(0.0, a.Percent));
                        today.RemainingHpByStage[a.Stage] = after;
                        
                        // If HP drops to near-zero, clamp to 0 for display but don't count as kill
                        if (after <= 0.0001)
                        {
                            today.RemainingHpByStage[a.Stage] = 0.0;
                        }
                    }

                    // Check for reset after each attack
                    if (unlockedStages.All(s => today.RemainingHpByStage[s] <= 0.0001))
                    {
                        foreach (var s in unlockedStages.ToArray())
                        {
                            today.RemainingHpByStage[s] = 100.0;
                        }
                    }
                }
                
                // Store kills from previous days in the model
                today.KillsPreviousDays = killsPreviousDays;

                Today = today;

                // Generate battle plan using the new engine
                BattlePlan = _engine.GenerateBattlePlan(players, today, MarginOfErrorPercent);
                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error parsing file: {ex.Message}";
                _logger.LogError(ex, "Error parsing Guild Battle CSV");
                return Page();
            }
        }

        public IActionResult OnPostExportAsync()
        {
            if (BattlePlan == null || BattlePlan.StageGroups.Count == 0)
            {
                ErrorMessage = "No battle plan to export. Analyze first.";
                return Page();
            }

            var sb = new StringBuilder();
            sb.AppendLine("Stage,Player");
            foreach (var group in BattlePlan.StageGroups)
            {
                foreach (var player in group.PlayerNames)
                {
                    sb.Append((int)group.Stage).Append(',');
                    sb.AppendLine(player);
                }
            }

            var utf8WithBom = new UTF8Encoding(true);
            using var memoryStream = new MemoryStream();
            using (var writer = new StreamWriter(memoryStream, utf8WithBom, leaveOpen: true))
            {
                writer.Write(sb.ToString());
            }
            var bytes = memoryStream.ToArray();
            var fileName = $"guild-battle-plan_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
    }
}
