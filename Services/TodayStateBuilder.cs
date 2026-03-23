using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public enum TodayStateBuildMode
    {
        OrderedReplay,
        OrderAgnosticAggregate
    }

    public sealed class StageHpReplayEntry
    {
        public int Day { get; set; }
        public int RowIndex { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public StageId Stage { get; set; }
        public bool Killed { get; set; }
        public double DamagePercent { get; set; }
        public double HpBefore { get; set; }
        public double HpAfter { get; set; }
        public bool TriggeredReset { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public sealed class StageHpComputationDebug
    {
        public int CurrentDay { get; set; }
        public string ComputationMethod { get; set; } = string.Empty;
        public int TotalAttemptsProcessed { get; set; }
        public int AttemptsOnCurrentDay { get; set; }
        public int RemainingHits { get; set; }
        public int ResetsDetectedWhileReplaying { get; set; }
        public bool Stage6UnlockedByS5Kills { get; set; }
        public int TotalS5KillsAcrossAllDays { get; set; }
        public Dictionary<StageId, double> InitialHpByStage { get; set; } = new();
        public Dictionary<StageId, double> TotalDamageByStage { get; set; } = new();
        public Dictionary<StageId, int> KillRowsByStage { get; set; } = new();
        public Dictionary<StageId, double> NetDamageByStage { get; set; } = new();
        public Dictionary<StageId, double> FinalHpByStage { get; set; } = new();
        public List<string> MissingKillWarnings { get; set; } = new();
        public List<StageHpReplayEntry> ReplayEntries { get; set; } = new();
    }

    public static class TodayStateBuilder
    {
        public static TodayState BuildTodayState(
            List<PlayerStageProfile> players,
            int currentDay,
            TodayStateBuildMode mode,
            out StageHpComputationDebug? debug)
        {
            return mode switch
            {
                TodayStateBuildMode.OrderAgnosticAggregate => BuildOrderAgnosticAggregate(players, currentDay, out debug),
                _ => BuildOrderedReplay(players, currentDay, out debug)
            };
        }

        private static TodayState BuildOrderedReplay(
            List<PlayerStageProfile> players,
            int currentDay,
            out StageHpComputationDebug? debug)
        {
            debug = null;

            var today = new TodayState
            {
                CurrentDay = currentDay,
                RemainingHpByStage = Enum.GetValues<StageId>().ToDictionary(s => s, s => 100.0),
                KillsToday = Enum.GetValues<StageId>().ToDictionary(s => s, s => 0)
            };

            int attemptsToday = 0;
            int totalS5KillsAllDays = 0;

            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                foreach (var attempt in player.Attempts)
                {
                    if (attempt.Day == currentDay) attemptsToday++;
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

            var killsPreviousDays = new Dictionary<StageId, int>();
            foreach (var s in Enum.GetValues<StageId>())
            {
                killsPreviousDays[s] = 0;
            }

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

                bool isToday = a.Day == currentDay;

                if (a.Killed)
                {
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
                    var remaining = today.RemainingHpByStage[a.Stage];
                    var after = Math.Max(0.0, remaining - Math.Max(0.0, a.Percent));
                    today.RemainingHpByStage[a.Stage] = after;

                    if (after <= 0.0001)
                    {
                        today.RemainingHpByStage[a.Stage] = 0.0;
                    }
                }

                if (unlockedStages.All(s => today.RemainingHpByStage[s] <= 0.0001))
                {
                    foreach (var s in unlockedStages.ToArray())
                    {
                        today.RemainingHpByStage[s] = 100.0;
                    }
                }
            }

            today.KillsPreviousDays = killsPreviousDays;
            return today;
        }

        private static TodayState BuildOrderAgnosticAggregate(
            List<PlayerStageProfile> players,
            int currentDay,
            out StageHpComputationDebug? debug)
        {
            const double killCompletionTolerance = 1.0;
            const double missingKillThreshold = 100.0;

            var today = new TodayState
            {
                CurrentDay = currentDay,
                RemainingHpByStage = Enum.GetValues<StageId>().ToDictionary(s => s, s => 100.0),
                KillsToday = Enum.GetValues<StageId>().ToDictionary(s => s, s => 0)
            };

            debug = new StageHpComputationDebug
            {
                CurrentDay = currentDay,
                ComputationMethod = "Day-aware order-agnostic estimate (current day only): remainingHP = clamp(100 - (totalDamageToday - 100 * killRowsToday), 0, 100); if killRowsToday > 0 and netDamage is near 0, stage is treated as killed (0 HP)."
            };

            int totalS5KillsAllDays = 0;

            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                foreach (var attempt in player.Attempts)
                {
                    if (attempt.Stage == StageId.S5 && attempt.Killed)
                        totalS5KillsAllDays++;
                }
            }

            today.IsStage6Unlocked = totalS5KillsAllDays >= 5;
            debug.TotalS5KillsAcrossAllDays = totalS5KillsAllDays;
            debug.Stage6UnlockedByS5Kills = today.IsStage6Unlocked;

            var unlockedStages = new HashSet<StageId>(Enum.GetValues<StageId>().Where(s => s != StageId.S6));
            if (today.IsStage6Unlocked) unlockedStages.Add(StageId.S6);

            foreach (var s in Enum.GetValues<StageId>())
                today.RemainingHpByStage[s] = unlockedStages.Contains(s) ? 100.0 : 0.0;

            foreach (var s in Enum.GetValues<StageId>())
                debug.InitialHpByStage[s] = today.RemainingHpByStage[s];

            var allAttempts = new List<AttemptLog>();
            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                foreach (var attempt in player.Attempts)
                    allAttempts.Add(attempt);
            }

            var totalDamageByStage = Enum.GetValues<StageId>().ToDictionary(s => s, _ => 0.0);
            var killRowsByStage = Enum.GetValues<StageId>().ToDictionary(s => s, _ => 0);

            foreach (var a in allAttempts)
            {
                if (!unlockedStages.Contains(a.Stage))
                {
                    debug.ReplayEntries.Add(new StageHpReplayEntry
                    {
                        Day = a.Day,
                        RowIndex = a.RowIndex,
                        PlayerName = a.PlayerName,
                        Stage = a.Stage,
                        Killed = a.Killed,
                        DamagePercent = a.Percent,
                        HpBefore = today.RemainingHpByStage.GetValueOrDefault(a.Stage, 0),
                        HpAfter = today.RemainingHpByStage.GetValueOrDefault(a.Stage, 0),
                        TriggeredReset = false,
                        Note = "Ignored: stage not unlocked at this point"
                    });
                    continue;
                }

                bool isToday = a.Day == currentDay;
                var damage = Math.Max(0.0, a.Percent);

                if (isToday)
                {
                    totalDamageByStage[a.Stage] += damage;
                    if (a.Killed)
                        killRowsByStage[a.Stage] += 1;
                }

                if (isToday && a.Killed)
                    today.KillsToday[a.Stage] += 1;

                string note;
                if (!isToday)
                {
                    note = $"Ignored for HP estimate: Day {a.Day} (using Day {currentDay} aggregate)";
                }
                else
                {
                    note = a.Killed
                        ? $"Aggregate contribution (Day {currentDay}): +{damage:F3}% damage, +1 kill row"
                        : $"Aggregate contribution (Day {currentDay}): +{damage:F3}% damage";
                }

                debug.ReplayEntries.Add(new StageHpReplayEntry
                {
                    Day = a.Day,
                    RowIndex = a.RowIndex,
                    PlayerName = a.PlayerName,
                    Stage = a.Stage,
                    Killed = a.Killed,
                    DamagePercent = damage,
                    HpBefore = 0,
                    HpAfter = 0,
                    TriggeredReset = false,
                    Note = note
                });
            }

            foreach (var stage in Enum.GetValues<StageId>())
            {
                if (!unlockedStages.Contains(stage))
                {
                    today.RemainingHpByStage[stage] = 0.0;
                    continue;
                }

                var totalDamage = totalDamageByStage.GetValueOrDefault(stage, 0);
                var killRows = killRowsByStage.GetValueOrDefault(stage, 0);
                var netDamage = totalDamage - (100.0 * killRows);
                var remainingHp = Math.Clamp(100.0 - netDamage, 0.0, 100.0);

                if (killRows > 0 && netDamage <= killCompletionTolerance)
                {
                    remainingHp = 0.0;
                }

                if (netDamage > missingKillThreshold)
                {
                    debug.MissingKillWarnings.Add($"S{(int)stage}: net damage is {netDamage:F3}% after subtracting {killRows} recorded kill row(s). Likely missing one or more kill checkboxes.");
                }
                else if (killRows == 0 && totalDamage > missingKillThreshold)
                {
                    debug.MissingKillWarnings.Add($"S{(int)stage}: total damage is {totalDamage:F3}% with 0 recorded kill rows. Likely missing kill checkbox(es).");
                }

                today.RemainingHpByStage[stage] = remainingHp;

                debug.TotalDamageByStage[stage] = totalDamage;
                debug.KillRowsByStage[stage] = killRows;
                debug.NetDamageByStage[stage] = netDamage;
            }

            debug.ResetsDetectedWhileReplaying = 0;

            int attemptsToday = 0;
            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                attemptsToday += player.Attempts.Count(a => a.Day == currentDay);
            }
            today.RemainingHits = Math.Max(0, 90 - attemptsToday);

            debug.TotalAttemptsProcessed = allAttempts.Count;
            debug.AttemptsOnCurrentDay = attemptsToday;
            debug.RemainingHits = today.RemainingHits;
            foreach (var s in Enum.GetValues<StageId>())
            {
                debug.TotalDamageByStage.TryAdd(s, 0);
                debug.KillRowsByStage.TryAdd(s, 0);
                debug.NetDamageByStage.TryAdd(s, 0);
                debug.FinalHpByStage[s] = today.RemainingHpByStage.GetValueOrDefault(s, 0);
            }

            return today;
        }
    }
}
