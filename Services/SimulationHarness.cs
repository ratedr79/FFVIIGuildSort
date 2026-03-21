using FFVIIEverCrisisAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class SimulationHarness
    {
        private readonly GuildBattleParser _parser = new();

        /// <summary>
        /// Run the simulation multiple times and aggregate results.
        /// </summary>
        public AggregatedTestResults RunMultiple(
            List<PlayerStageProfile> players,
            TodayState todayState,
            SimulationTestSettings settings,
            string scoreBy = "MinSumFinalHP")
        {
            var results = new AggregatedTestResults
            {
                TotalRuns = settings.NumberOfRuns,
                Settings = settings
            };

            var rng = new Random();

            for (int i = 0; i < settings.NumberOfRuns; i++)
            {
                int seed = settings.SeedMode switch
                {
                    "Fixed" => settings.FixedSeed,
                    "Incremental" => settings.FixedSeed + i,
                    _ => rng.Next() // Auto
                };

                // Create a fresh engine with the seed
                var engine = new GuildBattleAssignmentEngine(seed);
                
                // Reprocess averages if any filter toggle is disabled (default is all enabled)
                var playersForRun = (settings.EnableOutlierFilter && settings.EnableDeviationCap)
                    ? players // Use pre-processed data as-is (all filters enabled)
                    : ReprocessAverages(players, settings); // Recompute with toggled filters

                var plan = engine.GenerateBattlePlan(playersForRun, todayState, settings.MarginOfErrorPercent);

                // Calculate attempts available for this run
                int attemptsAvailable = 0;
                foreach (var p in playersForRun)
                {
                    var attemptsLeft = 3 - p.Attempts.Count(a => a.Day == todayState.CurrentDay);
                    if (attemptsLeft > 0) attemptsAvailable += attemptsLeft;
                }

                int attacksMade = plan.AttackLog.Count(a => !a.IsReset);

                var run = new SingleRunResult
                {
                    RunIndex = i + 1,
                    Seed = seed,
                    Resets = plan.ExpectedResets,
                    FinalHP = new Dictionary<StageId, double>(plan.FinalHpByStage),
                    StageClears = new Dictionary<StageId, int>(plan.StageClears),
                    Stage6Unlocked = todayState.IsStage6Unlocked,
                    AttackLog = plan.AttackLog,
                    TotalAttacks = attacksMade,
                    AttemptsAvailable = attemptsAvailable,
                    AttemptsUsed = attacksMade
                };

                // Extract assignments from stage groups
                foreach (var group in plan.StageGroups)
                {
                    run.Assignments[group.Stage] = new List<string>(group.PlayerNames);
                }

                results.Runs.Add(run);
            }

            // Aggregate statistics
            Aggregate(results, scoreBy);

            return results;
        }

        /// <summary>
        /// Run a single detailed simulation with assignment rationale.
        /// </summary>
        public AggregatedTestResults RunSingleDetailed(
            List<PlayerStageProfile> players,
            TodayState todayState,
            SimulationTestSettings settings)
        {
            settings.NumberOfRuns = 1;
            var results = RunMultiple(players, todayState, settings);

            // Build assignment rationales
            const double S6_THRESHOLD = 8.0;
            var rationales = new List<PlayerAssignmentRationale>();

            foreach (var player in players.OrderBy(p => p.Name))
            {
                var attemptsLeft = 3 - player.Attempts.Count(a => a.Day == todayState.CurrentDay);
                if (attemptsLeft <= 0) continue;

                var rationale = new PlayerAssignmentRationale
                {
                    PlayerName = player.Name,
                    AveragedPercents = new Dictionary<StageId, double>(player.AveragedPercents),
                };

                // Determine effective percents and margins (approximation - engine uses random margins)
                foreach (var stage in Enum.GetValues<StageId>())
                {
                    var avgPct = player.AveragedPercents.GetValueOrDefault(stage, 0);
                    rationale.AveragedPercents[stage] = avgPct;
                }

                // Determine assignment from the run
                var run = results.Runs.FirstOrDefault();
                if (run != null)
                {
                    foreach (var kvp in run.Assignments)
                    {
                        if (kvp.Value.Contains(player.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            rationale.AssignedStage = $"S{(int)kvp.Key}";
                        }
                    }
                }

                // Check S6 reservation
                var s6Pct = player.AveragedPercents.GetValueOrDefault(StageId.S6, 0);
                rationale.IsS6Reserved = s6Pct >= S6_THRESHOLD;

                if (rationale.IsS6Reserved)
                {
                    rationale.Reason = $"S6 reserved (avg {s6Pct:F1}% >= {S6_THRESHOLD}% threshold)";
                }
                else if (rationale.AssignedStage.Contains("5"))
                {
                    var s5Pct = player.AveragedPercents.GetValueOrDefault(StageId.S5, 0);
                    rationale.Reason = $"Assigned S5 (avg {s5Pct:F1}%)";
                }
                else if (rationale.AssignedStage.Contains("4"))
                {
                    var s4Pct = player.AveragedPercents.GetValueOrDefault(StageId.S4, 0);
                    rationale.Reason = $"Assigned S4 (avg {s4Pct:F1}%)";
                }
                else
                {
                    rationale.Reason = "Assigned S1-3 (remaining pool)";
                }

                rationales.Add(rationale);
            }

            results.Rationales = rationales;
            return results;
        }

        private void Aggregate(AggregatedTestResults results, string scoreBy = "MinSumFinalHP")
        {
            if (results.Runs.Count == 0) return;

            var resets = results.Runs.Select(r => (double)r.Resets).OrderBy(x => x).ToList();
            results.AvgResets = resets.Average();
            results.MinResets = resets.Min();
            results.MaxResets = resets.Max();
            results.P10Resets = Percentile(resets, 10);
            results.P90Resets = Percentile(resets, 90);

            // Per-stage aggregation
            foreach (var stage in Enum.GetValues<StageId>())
            {
                var finalHps = results.Runs.Select(r => r.FinalHP.GetValueOrDefault(stage, 0)).ToList();
                results.AvgFinalHP[stage] = finalHps.Average();

                var clears = results.Runs.Select(r => (double)r.StageClears.GetValueOrDefault(stage, 0)).ToList();
                results.AvgClears[stage] = clears.Average();

                // Clear rate: % of runs where stage was cleared at least once
                var clearedRuns = results.Runs.Count(r => r.StageClears.GetValueOrDefault(stage, 0) > 0);
                results.ClearRatePercent[stage] = results.Runs.Count > 0
                    ? (double)clearedRuns / results.Runs.Count * 100.0
                    : 0;
            }

            // Stage 6 unlock rate
            var s6Unlocked = results.Runs.Count(r => r.Stage6Unlocked);
            results.Stage6UnlockRatePercent = results.Runs.Count > 0
                ? (double)s6Unlocked / results.Runs.Count * 100.0
                : 0;

            // Assignment frequency
            var freq = new Dictionary<string, Dictionary<StageId, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var run in results.Runs)
            {
                foreach (var kvp in run.Assignments)
                {
                    foreach (var name in kvp.Value)
                    {
                        if (!freq.ContainsKey(name))
                        {
                            freq[name] = new Dictionary<StageId, int>();
                            foreach (var s in Enum.GetValues<StageId>())
                                freq[name][s] = 0;
                        }
                        freq[name][kvp.Key]++;
                    }
                }
            }
            results.AssignmentFrequency = freq;
            results.TotalPlayers = freq.Count;

            // Total simulated attacks
            results.TotalSimulatedAttacks = results.Runs.Sum(r => r.TotalAttacks);

            // Score runs to find Best/Worst
            // Lower score = better. More resets = more damage dealt = better.
            if (results.Runs.Count >= 2)
            {
                Func<SingleRunResult, (int ResetsPrimary, double Secondary)> scoreFunc = scoreBy switch
                {
                    "MinS6FinalHP" => r => (-r.Resets, r.FinalHP.GetValueOrDefault(StageId.S6, 100)),
                    "MaxTotalClears" => r => (-r.Resets, -r.StageClears.Values.Sum()),
                    _ => r => (-r.Resets, r.FinalHP.Values.Sum()) // MinSumFinalHP (default)
                };

                var scored = results.Runs
                    .Select(r => new { r.RunIndex, Score = scoreFunc(r) })
                    .OrderBy(x => x.Score.ResetsPrimary)
                    .ThenBy(x => x.Score.Secondary)
                    .ToList();

                results.BestRunIndex = scored.First().RunIndex;
                results.WorstRunIndex = scored.Last().RunIndex;
            }
            else if (results.Runs.Count == 1)
            {
                results.BestRunIndex = results.Runs[0].RunIndex;
                results.WorstRunIndex = results.Runs[0].RunIndex;
            }
        }

        private static double Percentile(List<double> sorted, int percentile)
        {
            if (sorted.Count == 0) return 0;
            if (sorted.Count == 1) return sorted[0];
            double index = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = Math.Min(lower + 1, sorted.Count - 1);
            double weight = index - lower;
            return sorted[lower] * (1 - weight) + sorted[upper] * weight;
        }

        /// <summary>
        /// Reprocess averaged percentages without outlier filter and/or deviation cap.
        /// Returns a deep copy of players with recalculated averages.
        /// </summary>
        private List<PlayerStageProfile> ReprocessAverages(
            List<PlayerStageProfile> players,
            SimulationTestSettings settings)
        {
            var result = new List<PlayerStageProfile>();
            foreach (var player in players)
            {
                var copy = new PlayerStageProfile
                {
                    Name = player.Name,
                    MockPercents = new Dictionary<StageId, double>(player.MockPercents),
                    Attempts = player.Attempts, // Share reference (read-only)
                    Notes = player.Notes,
                    AveragedPercents = new Dictionary<StageId, double>()
                };

                foreach (var stage in Enum.GetValues<StageId>())
                {
                    var mockPercent = player.MockPercents.GetValueOrDefault(stage, 0);

                    var liveAttacks = player.Attempts
                        .Where(a => a.Stage == stage && a.Percent > 0.001);

                    // Apply outlier filter only if enabled
                    if (settings.EnableOutlierFilter)
                    {
                        liveAttacks = liveAttacks.Where(a => !(a.Killed && a.Percent < 3.0));
                    }

                    var liveList = liveAttacks.Select(a => a.Percent).ToList();

                    double averagedPercent;
                    if (liveList.Any())
                    {
                        var liveAverage = liveList.Average();
                        averagedPercent = (mockPercent * 0.4) + (liveAverage * 0.6);

                        // Apply deviation cap only if enabled
                        if (settings.EnableDeviationCap)
                        {
                            var minimumAllowed = mockPercent * 0.7;
                            if (averagedPercent < minimumAllowed)
                                averagedPercent = minimumAllowed;
                        }
                    }
                    else
                    {
                        averagedPercent = mockPercent;
                    }

                    copy.AveragedPercents[stage] = averagedPercent;
                }

                result.Add(copy);
            }
            return result;
        }
    }
}
