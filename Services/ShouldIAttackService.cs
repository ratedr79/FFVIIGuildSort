using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class ShouldIAttackService
    {
        private readonly StagePointEstimator _pointEstimator;
        private readonly IDispatcherRunner _dispatcherRunner;
        private readonly ILogger<ShouldIAttackService> _logger;

        public ShouldIAttackService(
            StagePointEstimator pointEstimator,
            IDispatcherRunner dispatcherRunner,
            ILogger<ShouldIAttackService> logger)
        {
            _pointEstimator = pointEstimator;
            _dispatcherRunner = dispatcherRunner;
            _logger = logger;
        }

        public async Task<ShouldIAttackRecommendationResult> AnalyzePlayerAsync(
            List<PlayerStageProfile> players,
            TodayState today,
            string googleSheetUrl,
            string playerName,
            bool immediateMode,
            CancellationToken cancellationToken = default)
        {
            var result = new ShouldIAttackRecommendationResult
            {
                PlayerName = playerName,
                CurrentDay = today.CurrentDay,
                RemainingHits = today.RemainingHits,
                ImmediateMode = immediateMode,
                AssumedCurrentHp = new Dictionary<StageId, double>(today.RemainingHpByStage),
                Warning = "Assumed current stage HP comes from spreadsheet-derived state. This recommendation is simulation-based; exact boss HP and per-run attack percentages can differ. If you have concerns, please check with guild leadership."
            };

            var dispatcher = await _dispatcherRunner.RunAsync(googleSheetUrl, today.CurrentDay, cancellationToken);
            var usedFallback = !dispatcher.Success || dispatcher.ParsedPlan == null;
            result.DispatcherUsed = dispatcher.Success && dispatcher.ParsedPlan != null;
            result.FallbackUsed = usedFallback;

            if (usedFallback && !string.IsNullOrWhiteSpace(dispatcher.Error))
            {
                _logger.LogWarning("ShouldIAttack dispatcher fallback for {Player}: {Error}", playerName, dispatcher.Error);
            }

            const int runs = 25;
            var rng = new Random();
            var prioritizedPlayers = PrioritizePlayerInList(players, playerName);

            for (var i = 0; i < runs; i++)
            {
                var seed = rng.Next();
                var engine = new GuildBattleAssignmentEngine(seed, _pointEstimator);
                var todayClone = CloneTodayState(today);

                BattlePlanSummary plan;
                if (!usedFallback && dispatcher.ParsedPlan != null)
                {
                    var planInput = CloneDispatcherPlan(dispatcher.ParsedPlan);
                    MovePlayerToTop(planInput, playerName);
                    plan = engine.SimulateWithFixedAssignments(planInput, prioritizedPlayers, todayClone, marginOfErrorPercent: 0, overshootTriggerPercent: 20, cleanupConfidenceBufferPercent: 15);
                }
                else
                {
                    plan = engine.GenerateBattlePlan(prioritizedPlayers, todayClone, marginOfErrorPercent: 0, overshootTriggerPercent: 20, cleanupConfidenceBufferPercent: 15);
                }

                var runEvidence = BuildRunEvidence(plan, playerName, i + 1, seed, !usedFallback);
                result.Runs.Add(runEvidence);
            }

            var bestRun = result.Runs
                    .FirstOrDefault();

            if (immediateMode)
            {
                var strictFirstHitRuns = result.Runs
                    .Where(run => RunHasSelectedPlayerFirstNonReset(run, playerName))
                    .ToList();

                var candidateRuns = strictFirstHitRuns.Count > 0 ? strictFirstHitRuns : result.Runs;
                bestRun = candidateRuns
                    .OrderByDescending(RunRankingKeyClears)
                    .ThenBy(RunRankingKeyFinalHp)
                    .ThenByDescending(RunRankingKeyPoints)
                    .FirstOrDefault();
            }
            else
            {
                bestRun = result.Runs
                    .OrderBy(RunRankingKeyEarliestPlayerAttack)
                    .ThenByDescending(RunRankingKeyResets)
                    .ThenBy(RunRankingKeyFinalHp)
                    .FirstOrDefault();
            }

            result.BestRun = bestRun;
            result.AssignedStage = ResolveAssignedStage(bestRun, playerName);
            result.SimulatedAttackStagesSummary = BuildSimulatedAttackStagesSummary(bestRun, playerName);

            if (bestRun == null)
            {
                result.AttackNow = false;
                result.Rationale = "No simulation evidence available for this player. Hold and verify sheet data.";
                return result;
            }

            if (immediateMode)
            {
                var runAlignedStage = result.AssignedStage;
                var heuristicImmediate = BuildImmediateRecommendation(players, today, playerName, result.AssignedStage);
                var immediate = runAlignedStage.HasValue
                    ? BuildRunAlignedImmediateRecommendation(players, today, playerName, runAlignedStage.Value, heuristicImmediate)
                    : heuristicImmediate;

                result.ImmediateRecommendation = immediate;
                result.AttackNow = true;
                result.RunAlignedImmediateStage = runAlignedStage;
                result.HeuristicFallbackImmediateStage = heuristicImmediate.Stage;
                result.ImmediateUsedHeuristicFallback = !runAlignedStage.HasValue;
                var strictFirstHitSatisfied = bestRun != null && RunHasSelectedPlayerFirstNonReset(bestRun, playerName);
                result.StrictFirstHitSatisfied = strictFirstHitSatisfied;
                result.Rationale = strictFirstHitSatisfied
                    ? $"Immediate-use mode enabled. Selected run requires your player to take the first non-reset attack, then favors highest clears, lowest final HP sum, then highest points. Recommended immediate stage is S{(int)immediate.Stage} ({immediate.Confidence} confidence)."
                    : $"Immediate-use mode enabled. No run had your player take the first non-reset attack, so fallback ranking used highest clears, lowest final HP sum, then highest points. Recommended immediate stage is S{(int)immediate.Stage} ({immediate.Confidence} confidence).";
                return result;
            }

            result.AttackNow = !bestRun.FullResetSeen || bestRun.PlayerAttacksWithinHorizon;
            result.Rationale = result.AttackNow
                ? "Selected run shows the player attacking before the first full reset, or no full reset was predicted."
                : "Selected run does not show the player attacking before the first full reset. Hold unless immediate-use is required.";

            return result;
        }

        public async Task<List<ShouldIAttackBulkRow>> AnalyzeAllPlayersAsync(
            List<PlayerStageProfile> players,
            TodayState today,
            string googleSheetUrl,
            CancellationToken cancellationToken = default)
        {
            var rows = new List<ShouldIAttackBulkRow>();
            foreach (var player in players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var rec = await AnalyzePlayerAsync(players, today, googleSheetUrl, player.Name, immediateMode: false, cancellationToken);
                rows.Add(new ShouldIAttackBulkRow
                {
                    PlayerName = player.Name,
                    Recommendation = rec.RecommendationLabel,
                    AssignedStage = rec.AssignedStage.HasValue ? $"S{(int)rec.AssignedStage.Value}" : "-",
                    Confidence = rec.AttackNow ? "Medium" : "Low",
                    DispatcherUsed = rec.DispatcherUsed,
                    FallbackUsed = rec.FallbackUsed,
                    BestRunResets = rec.BestRun?.Resets ?? 0,
                    BestRunFinalHpSum = rec.BestRun?.FinalHpSum ?? 0,
                    Rationale = rec.Rationale
                });
            }

            return rows;
        }

        public static int DetectCurrentDay(List<PlayerStageProfile> players)
        {
            const int maxHitsPerDay = 90;
            const int maxDays = 3;

            var maxDay = players
                .SelectMany(p => p.Attempts ?? new List<AttemptLog>())
                .Where(a => a.Day >= 1 && a.Day <= maxDays)
                .Select(a => a.Day)
                .DefaultIfEmpty(1)
                .Max();

            var attemptsOnMaxDay = players
                .SelectMany(p => p.Attempts ?? new List<AttemptLog>())
                .Count(a => a.Day == maxDay);

            if (attemptsOnMaxDay >= maxHitsPerDay && maxDay < maxDays)
            {
                return maxDay + 1;
            }

            return Math.Clamp(maxDay, 1, maxDays);
        }

        public static int RunRankingKeyResets(ShouldIAttackRunEvidence run) => run.Resets;
        public static int RunRankingKeyClears(ShouldIAttackRunEvidence run) => run.TotalClears;
        public static double RunRankingKeyFinalHp(ShouldIAttackRunEvidence run) => run.FinalHpSum;
        public static double RunRankingKeyPoints(ShouldIAttackRunEvidence run) => run.TotalEstimatedPoints;
        public static int RunRankingKeyEarliestPlayerAttack(ShouldIAttackRunEvidence run) => run.EarliestPlayerAttackOrder;
        public static bool RunHasSelectedPlayerFirstNonReset(ShouldIAttackRunEvidence run, string playerName)
            => AttackLogHasSelectedPlayerFirstNonReset(run.AttackLog, playerName);

        public static DispatcherParsedPlan PrioritizePlayerInPlan(DispatcherParsedPlan sourcePlan, string playerName)
        {
            var clone = CloneDispatcherPlan(sourcePlan);
            MovePlayerToTop(clone, playerName);
            return clone;
        }

        public static (bool PlayerAttacksWithinHorizon, int HorizonAttackCap, bool FullResetSeen, int EarliestPlayerAttackOrder) EvaluateHorizon(
            IReadOnlyList<AttackLogEntry> attackLog,
            string playerName)
        {
            var attackOrder = 0;
            var firstAttackOrder = int.MaxValue;
            var firstResetOrder = int.MaxValue;

            foreach (var entry in attackLog)
            {
                if (entry.IsReset)
                {
                    if (firstResetOrder == int.MaxValue && entry.PlayerName.Contains("=== RESET #", StringComparison.OrdinalIgnoreCase))
                    {
                        firstResetOrder = attackOrder;
                    }

                    continue;
                }

                attackOrder++;
                if (firstAttackOrder == int.MaxValue && string.Equals(entry.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                {
                    firstAttackOrder = attackOrder;
                }
            }

            var hasFullReset = firstResetOrder != int.MaxValue;
            var horizon = hasFullReset ? firstResetOrder : attackOrder;
            var within = firstAttackOrder != int.MaxValue && firstAttackOrder <= horizon;

            return (within, horizon, hasFullReset, firstAttackOrder);
        }

        public static bool AttackLogHasSelectedPlayerFirstNonReset(IReadOnlyList<AttackLogEntry> attackLog, string playerName)
        {
            var firstNonReset = attackLog.FirstOrDefault(a => !a.IsReset);
            return firstNonReset != null && string.Equals(firstNonReset.PlayerName, playerName, StringComparison.OrdinalIgnoreCase);
        }

        private static TodayState CloneTodayState(TodayState source)
        {
            return new TodayState
            {
                CurrentDay = source.CurrentDay,
                RemainingHits = source.RemainingHits,
                IsStage6Unlocked = source.IsStage6Unlocked,
                RemainingHpByStage = new Dictionary<StageId, double>(source.RemainingHpByStage),
                KillsToday = new Dictionary<StageId, int>(source.KillsToday),
                KillsPreviousDays = new Dictionary<StageId, int>(source.KillsPreviousDays)
            };
        }

        private static DispatcherParsedPlan CloneDispatcherPlan(DispatcherParsedPlan source)
        {
            var clone = new DispatcherParsedPlan
            {
                ExpectedResets = source.ExpectedResets,
                RawOutput = source.RawOutput
            };

            foreach (var stage in Enum.GetValues<StageId>())
            {
                var list = source.StageAssignments.GetValueOrDefault(stage) ?? new List<DispatcherPlayerAssignment>();
                clone.StageAssignments[stage] = list
                    .Select(p => new DispatcherPlayerAssignment
                    {
                        PlayerName = p.PlayerName,
                        Attacks = p.Attacks
                    })
                    .ToList();
            }

            return clone;
        }

        private static void MovePlayerToTop(DispatcherParsedPlan plan, string playerName)
        {
            foreach (var stage in plan.StageAssignments.Keys.ToList())
            {
                var list = plan.StageAssignments[stage];
                var idx = list.FindIndex(p => string.Equals(p.PlayerName, playerName, StringComparison.OrdinalIgnoreCase));
                if (idx <= 0)
                {
                    continue;
                }

                var player = list[idx];
                list.RemoveAt(idx);
                list.Insert(0, player);
            }
        }

        private static List<PlayerStageProfile> PrioritizePlayerInList(List<PlayerStageProfile> players, string playerName)
        {
            var prioritized = players
                .Select(p => p)
                .OrderByDescending(p => string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return prioritized;
        }

        private static ShouldIAttackRunEvidence BuildRunEvidence(BattlePlanSummary plan, string playerName, int runIndex, int seed, bool usedDispatcher)
        {
            var finalHpSum = plan.FinalHpByStage.Values.Sum();
            var totalClears = plan.StageClears.Values.Sum();
            var totalEstimatedPoints = plan.AttackLog
                .Where(a => !a.IsReset)
                .Sum(a => a.EstimatedPoints + a.BonusPoints);
            var firstAttackOrder = int.MaxValue;
            StageId? firstAttackStage = null;
            foreach (var entry in plan.AttackLog)
            {
                if (entry.IsReset)
                {
                    continue;
                }

                if (firstAttackOrder == int.MaxValue && string.Equals(entry.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                {
                    firstAttackStage = entry.Stage;
                }
            }

            var horizonDecision = EvaluateHorizon(plan.AttackLog, playerName);
            firstAttackOrder = horizonDecision.EarliestPlayerAttackOrder;

            return new ShouldIAttackRunEvidence
            {
                RunIndex = runIndex,
                Seed = seed,
                Resets = plan.ExpectedResets,
                TotalClears = totalClears,
                FinalHpSum = finalHpSum,
                TotalEstimatedPoints = totalEstimatedPoints,
                EarliestPlayerAttackOrder = firstAttackOrder,
                EarliestPlayerAttackStage = firstAttackStage,
                FullResetSeen = horizonDecision.FullResetSeen,
                HorizonAttackCap = horizonDecision.HorizonAttackCap,
                PlayerAttacksWithinHorizon = horizonDecision.PlayerAttacksWithinHorizon,
                UsedDispatcher = usedDispatcher,
                FinalHpByStage = new Dictionary<StageId, double>(plan.FinalHpByStage),
                AttackLog = plan.AttackLog
            };
        }

        private static StageId? ResolveAssignedStage(ShouldIAttackRunEvidence? run, string playerName)
        {
            if (run?.EarliestPlayerAttackStage != null)
            {
                return run.EarliestPlayerAttackStage;
            }

            return null;
        }

        private static string BuildSimulatedAttackStagesSummary(ShouldIAttackRunEvidence? run, string playerName)
        {
            if (run == null)
            {
                return "No simulated attacks found.";
            }

            var playerAttacks = run.AttackLog
                .Where(a => !a.IsReset)
                .Where(a => string.Equals(a.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (playerAttacks.Count == 0)
            {
                return "No simulated attacks found for selected player in this run.";
            }

            var stageGroups = playerAttacks
                .GroupBy(a => a.Stage)
                .OrderByDescending(g => (int)g.Key)
                .Select(g => $"S{(int)g.Key} x{g.Count()}")
                .ToList();

            if (stageGroups.Count == 1)
            {
                return $"Simulated attacks: {stageGroups[0]}.";
            }

            return $"Simulated split attacks: {string.Join(", ", stageGroups)}.";
        }

        private static ShouldIAttackImmediateRecommendation BuildImmediateRecommendation(
            List<PlayerStageProfile> players,
            TodayState today,
            string playerName,
            StageId? assignedStage)
        {
            var player = players.FirstOrDefault(p => string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                return new ShouldIAttackImmediateRecommendation
                {
                    Stage = assignedStage ?? StageId.S1,
                    Confidence = "Low",
                    Rationale = "Player profile not found."
                };
            }

            var unlockedStages = Enum.GetValues<StageId>()
                .Where(s => s != StageId.S6 || today.IsStage6Unlocked)
                .Where(s => today.RemainingHpByStage.GetValueOrDefault(s, 0) > 0.001)
                .OrderByDescending(s => (int)s)
                .ToList();

            var viable = unlockedStages
                .Select(s => new
                {
                    Stage = s,
                    Avg = player.AveragedPercents.GetValueOrDefault(s, 0),
                    Hp = today.RemainingHpByStage.GetValueOrDefault(s, 0)
                })
                .Where(x => x.Avg > 0.001)
                .OrderByDescending(x => x.Stage)
                .ThenBy(x => Math.Abs(x.Avg - x.Hp))
                .FirstOrDefault();

            if (viable == null)
            {
                return new ShouldIAttackImmediateRecommendation
                {
                    Stage = assignedStage ?? StageId.S1,
                    ExpectedDamagePercent = 0,
                    Confidence = "Low",
                    DiffersFromAssignedExpectation = assignedStage.HasValue,
                    Rationale = "No viable stage with positive expected damage was found."
                };
            }

            var confidence = viable.Avg >= viable.Hp ? "High" : viable.Avg >= viable.Hp * 0.6 ? "Medium" : "Low";

            return new ShouldIAttackImmediateRecommendation
            {
                Stage = viable.Stage,
                ExpectedDamagePercent = viable.Avg,
                Confidence = confidence,
                DiffersFromAssignedExpectation = assignedStage.HasValue && assignedStage.Value != viable.Stage,
                Rationale = $"Highest viable available stage with expected damage {viable.Avg:F1}% against {viable.Hp:F1}% remaining HP."
            };
        }

        private static ShouldIAttackImmediateRecommendation BuildRunAlignedImmediateRecommendation(
            List<PlayerStageProfile> players,
            TodayState today,
            string playerName,
            StageId runAlignedStage,
            ShouldIAttackImmediateRecommendation heuristicImmediate)
        {
            var player = players.FirstOrDefault(p => string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase));
            var avg = player?.AveragedPercents.GetValueOrDefault(runAlignedStage, 0) ?? 0;
            var hp = today.RemainingHpByStage.GetValueOrDefault(runAlignedStage, 0);
            var confidence = avg >= hp ? "High" : avg >= hp * 0.6 ? "Medium" : "Low";

            return new ShouldIAttackImmediateRecommendation
            {
                Stage = runAlignedStage,
                ExpectedDamagePercent = avg,
                Confidence = confidence,
                DiffersFromAssignedExpectation = false,
                Rationale = $"Run-aligned recommendation from selected simulation. First simulated attack for selected player is on S{(int)runAlignedStage}. Heuristic-only stage was S{(int)heuristicImmediate.Stage}."
            };
        }
    }
}
