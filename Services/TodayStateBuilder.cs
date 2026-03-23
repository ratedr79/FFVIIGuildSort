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
        public Dictionary<StageId, int> EffectiveResetsByStage { get; set; } = new();
        public Dictionary<StageId, double> NetDamageByStage { get; set; } = new();
        public Dictionary<StageId, double> FinalHpByStage { get; set; } = new();
        public List<string> MissingKillWarnings { get; set; } = new();
        public List<string> DayTransitionNotes { get; set; } = new();
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

            var today = new TodayState
            {
                CurrentDay = currentDay,
                RemainingHpByStage = Enum.GetValues<StageId>().ToDictionary(s => s, s => 100.0),
                KillsToday = Enum.GetValues<StageId>().ToDictionary(s => s, s => 0)
            };

            debug = new StageHpComputationDebug
            {
                CurrentDay = currentDay,
                ComputationMethod = $"Day-by-day order-agnostic aggregate (days 1..{currentDay}). " +
                    "Per day: dayResets = min(kills across all stages in reset cycle). " +
                    "Per stage: remainingHP = clamp(startHP + 100 × dayResets − totalDamage, 0, 100). " +
                    "Between days: stages at 0% HP revive to 100%."
            };

            // Collect all attempts
            var allAttempts = new List<AttemptLog>();
            int totalS5KillsAllDays = 0;
            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                foreach (var attempt in player.Attempts)
                {
                    allAttempts.Add(attempt);
                    if (attempt.Stage == StageId.S5 && attempt.Killed)
                        totalS5KillsAllDays++;
                }
            }

            today.IsStage6Unlocked = totalS5KillsAllDays >= 5;
            debug.TotalS5KillsAcrossAllDays = totalS5KillsAllDays;
            debug.Stage6UnlockedByS5Kills = today.IsStage6Unlocked;

            var unlockedStages = new HashSet<StageId>(Enum.GetValues<StageId>().Where(s => s != StageId.S6));
            if (today.IsStage6Unlocked) unlockedStages.Add(StageId.S6);

            // Group attempts by day
            var attemptsByDay = allAttempts
                .GroupBy(a => a.Day)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Initialize HP — all unlocked stages start at 100% on Day 1
            var stageHp = new Dictionary<StageId, double>();
            foreach (var s in Enum.GetValues<StageId>())
                stageHp[s] = unlockedStages.Contains(s) ? 100.0 : 0.0;

            // Track cumulative S5 kills to detect when S6 unlocks
            int cumulativeS5Kills = 0;
            bool s6UnlockedBeforeDay = false;

            // Process each day from 1 to currentDay
            for (int day = 1; day <= currentDay; day++)
            {
                var dayAttempts = attemptsByDay.GetValueOrDefault(day, new List<AttemptLog>());

                var dayDamage = Enum.GetValues<StageId>().ToDictionary(s => s, _ => 0.0);
                var dayKills = Enum.GetValues<StageId>().ToDictionary(s => s, _ => 0);

                foreach (var a in dayAttempts)
                {
                    if (!unlockedStages.Contains(a.Stage)) continue;
                    dayDamage[a.Stage] += Math.Max(0.0, a.Percent);
                    if (a.Killed)
                        dayKills[a.Stage]++;
                }

                // Detect whether S6 unlocked during this day
                bool s6UnlockedDuringThisDay = false;
                if (today.IsStage6Unlocked && !s6UnlockedBeforeDay)
                {
                    cumulativeS5Kills += dayKills[StageId.S5];
                    if (cumulativeS5Kills >= 5)
                        s6UnlockedDuringThisDay = true;
                }

                // Compute dayResets: the number of full clears (all stages in the
                // reset cycle killed) that occurred this day.
                // A reset requires ALL unlocked stages to be dead simultaneously.
                // dayResets = min(kills) across all stages in the current reset cycle.
                // Special case: if S6 unlocked mid-day, the reset cycle changed
                // partway through — fall back to per-stage kills as an approximation.
                bool usePerStageKillsFallback = s6UnlockedDuringThisDay;
                int dayResets = 0;
                if (!usePerStageKillsFallback)
                {
                    var resetCycleStages = s6UnlockedBeforeDay
                        ? Enum.GetValues<StageId>().Where(s => unlockedStages.Contains(s))
                        : Enum.GetValues<StageId>().Where(s => s != StageId.S6 && unlockedStages.Contains(s));
                    dayResets = resetCycleStages.Any()
                        ? resetCycleStages.Min(s => dayKills[s])
                        : 0;
                }

                // Capture start-of-current-day HP for debug display
                if (day == currentDay)
                {
                    foreach (var s in Enum.GetValues<StageId>())
                        debug.InitialHpByStage[s] = stageHp[s];
                }

                // Apply per-stage aggregate formula for this day
                foreach (var stage in Enum.GetValues<StageId>())
                {
                    if (!unlockedStages.Contains(stage)) continue;

                    var startHp = stageHp[stage];
                    int effectiveResets = usePerStageKillsFallback
                        ? dayKills[stage]
                        : dayResets;
                    var rawRemaining = startHp + 100.0 * effectiveResets - dayDamage[stage];
                    var remaining = Math.Clamp(rawRemaining, 0.0, 100.0);

                    // Snap near-zero to 0 when kills occurred
                    if (dayKills[stage] > 0 && remaining < killCompletionTolerance)
                        remaining = 0.0;

                    // Missing-kill detection: if raw remaining < 0, damage exceeds
                    // what recorded kills can account for
                    if (rawRemaining < -killCompletionTolerance)
                    {
                        debug.MissingKillWarnings.Add(
                            $"S{(int)stage} Day {day}: computed HP = {rawRemaining:F3}% " +
                            $"(start {startHp:F3}% + {effectiveResets} resets × 100% − {dayDamage[stage]:F3}% damage). " +
                            "Likely missing kill checkbox(es).");
                    }

                    stageHp[stage] = remaining;

                    // Store current-day stats for the debug table
                    if (day == currentDay)
                    {
                        debug.TotalDamageByStage[stage] = dayDamage[stage];
                        debug.KillRowsByStage[stage] = dayKills[stage];
                        debug.EffectiveResetsByStage[stage] = effectiveResets;
                        debug.NetDamageByStage[stage] = dayDamage[stage] - 100.0 * effectiveResets;
                    }
                }

                // Update S6 unlock tracking for next day
                if (!s6UnlockedBeforeDay && (s6UnlockedDuringThisDay || cumulativeS5Kills >= 5))
                    s6UnlockedBeforeDay = true;

                // Day transition: dead stages revive to 100% for the next day
                if (day < currentDay)
                {
                    var revived = new List<string>();
                    var carried = new List<string>();
                    foreach (var stage in Enum.GetValues<StageId>())
                    {
                        if (!unlockedStages.Contains(stage)) continue;
                        if (stageHp[stage] <= 0.0)
                        {
                            revived.Add($"S{(int)stage} (0% → 100%)");
                            stageHp[stage] = 100.0;
                        }
                        else if (stageHp[stage] < 100.0)
                        {
                            carried.Add($"S{(int)stage} ({stageHp[stage]:F3}% carried over)");
                        }
                    }
                    var parts = new List<string>();
                    if (revived.Any()) parts.Add("Revived: " + string.Join(", ", revived));
                    if (carried.Any()) parts.Add("Carried: " + string.Join(", ", carried));
                    if (parts.Any())
                        debug.DayTransitionNotes.Add($"Day {day} → Day {day + 1}: {string.Join(". ", parts)}");
                }
            }

            // Final state
            foreach (var stage in Enum.GetValues<StageId>())
            {
                today.RemainingHpByStage[stage] = stageHp[stage];
                debug.FinalHpByStage[stage] = stageHp[stage];
            }

            // Count current-day attempts and kills
            int attemptsToday = 0;
            foreach (var player in players)
            {
                if (player.Attempts == null) continue;
                foreach (var a in player.Attempts)
                {
                    if (a.Day == currentDay)
                    {
                        attemptsToday++;
                        if (a.Killed)
                            today.KillsToday[a.Stage]++;
                    }
                }
            }
            today.RemainingHits = Math.Max(0, 90 - attemptsToday);

            debug.TotalAttemptsProcessed = allAttempts.Count;
            debug.AttemptsOnCurrentDay = attemptsToday;
            debug.RemainingHits = today.RemainingHits;
            debug.ResetsDetectedWhileReplaying = 0;

            // Ensure all debug dictionaries have entries for every stage
            foreach (var s in Enum.GetValues<StageId>())
            {
                debug.TotalDamageByStage.TryAdd(s, 0);
                debug.KillRowsByStage.TryAdd(s, 0);
                debug.EffectiveResetsByStage.TryAdd(s, 0);
                debug.NetDamageByStage.TryAdd(s, 0);
                debug.InitialHpByStage.TryAdd(s, 0);
                debug.FinalHpByStage.TryAdd(s, stageHp.GetValueOrDefault(s, 0));
            }

            // Build replay entries for reference
            foreach (var a in allAttempts)
            {
                bool isToday = a.Day == currentDay;
                var damage = Math.Max(0.0, a.Percent);
                string note;

                if (!unlockedStages.Contains(a.Stage))
                {
                    note = "Ignored: stage not unlocked";
                }
                else if (!isToday)
                {
                    note = $"Day {a.Day} aggregate: +{damage:F3}% damage" +
                           (a.Killed ? ", +1 kill row (processed in day-by-day carryover)" : " (processed in day-by-day carryover)");
                }
                else
                {
                    note = a.Killed
                        ? $"Day {currentDay} aggregate: +{damage:F3}% damage, +1 kill row"
                        : $"Day {currentDay} aggregate: +{damage:F3}% damage";
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

            return today;
        }
    }
}
