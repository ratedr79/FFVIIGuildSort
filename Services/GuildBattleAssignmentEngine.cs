using FFVIIEverCrisisAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class GuildBattleAssignmentEngine
    {
        private readonly Random _random;
        
        public GuildBattleAssignmentEngine()
        {
            _random = new Random();
        }
        
        public GuildBattleAssignmentEngine(int seed)
        {
            _random = new Random(seed);
        }

        private double CalculateSmartMargin(PlayerStageProfile player, StageId stage, double baseMargin)
        {
            // Get mock percentages for current and adjacent stages
            player.MockPercents.TryGetValue(stage, out var currentPct);
            
            // Check next stage to gauge confidence
            StageId? nextStage = stage switch
            {
                StageId.S1 => StageId.S2,
                StageId.S2 => StageId.S3,
                StageId.S3 => StageId.S4,
                StageId.S4 => StageId.S5,
                StageId.S5 => StageId.S6,
                StageId.S6 => null,
                _ => null
            };
            
            double nextPct = 0;
            if (nextStage.HasValue)
            {
                player.AveragedPercents.TryGetValue(nextStage.Value, out nextPct);
            }
            
            // If player has 100% on both current and next stage, they're very confident - minimal margin
            bool veryConfident = currentPct >= 99.9 && nextPct >= 99.9;
            
            // Stage difficulty scaling (Stage 1 easiest, Stage 6 hardest)
            double stageDifficultyFactor = stage switch
            {
                StageId.S1 => 0.0,  // Easiest - no margin if confident
                StageId.S2 => 0.1,  // Very easy
                StageId.S3 => 0.2,  // Easy
                StageId.S4 => 0.5,  // Medium
                StageId.S5 => 0.8,  // Hard
                StageId.S6 => 1.0,  // Hardest - full margin
                _ => 1.0
            };
            
            // Add randomness to simulate variance in player performance
            // Random multiplier between 0.0 (perfect battle) and 1.0 (poor battle)
            double performanceVariance = _random.NextDouble();
            
            double calculatedMargin;
            
            // If very confident, use minimal margin scaled by stage difficulty
            if (veryConfident)
            {
                calculatedMargin = baseMargin * stageDifficultyFactor * 0.2; // Max 20% of base margin for confident players
            }
            // If current stage is 100% but next isn't, use reduced margin
            else if (currentPct >= 99.9)
            {
                calculatedMargin = baseMargin * stageDifficultyFactor * 0.5; // 50% of base margin
            }
            // Otherwise, scale margin by stage difficulty
            else
            {
                calculatedMargin = baseMargin * stageDifficultyFactor;
            }
            
            // Apply performance variance
            return calculatedMargin * performanceVariance;
        }

        public BattlePlanSummary GenerateBattlePlan(
            List<PlayerStageProfile> players,
            TodayState todayState,
            double marginOfErrorPercent)
        {
            var plan = new BattlePlanSummary();
            
            // Build effective percentages for each player with smart margin calculation
            var playerData = new List<(string name, int attempts, Dictionary<StageId, double> eff, Dictionary<StageId, double> avgPct)>();
            
            foreach (var p in players)
            {
                var attemptsLeft = 3 - p.Attempts.Count(a => a.Day == todayState.CurrentDay);
                if (attemptsLeft <= 0) continue;
                
                var effMap = new Dictionary<StageId, double>();
                var avgPctMap = new Dictionary<StageId, double>();
                foreach (var stage in Enum.GetValues<StageId>())
                {
                    // Use averaged percentages (mock + live attacks) instead of just mock
                    p.AveragedPercents.TryGetValue(stage, out var pct);
                    avgPctMap[stage] = pct; // Store original averaged percentage
                    
                    // Calculate smart margin based on player's confidence on this and adjacent stages
                    double smartMargin = CalculateSmartMargin(p, stage, marginOfErrorPercent);
                    
                    effMap[stage] = Math.Max(0.0, pct - smartMargin);
                }
                playerData.Add((p.Name, attemptsLeft, effMap, avgPctMap));
            }

            // Initial stage assignments (will be refined based on simulation)
            var stageAssignments = new Dictionary<StageId, List<string>>
            {
                { StageId.S1, new List<string>() },
                { StageId.S2, new List<string>() },
                { StageId.S3, new List<string>() },
                { StageId.S4, new List<string>() },
                { StageId.S5, new List<string>() },
                { StageId.S6, new List<string>() }
            };

            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // PRIORITY 1: Reserve Stage 6 capable players (top scorers, relative selection)
            // Dynamic selection based on the top S6 scorer instead of a fixed threshold
            const double S6_MIN_FLOOR = 2.0;         // Absolute minimum to even consider S6
            const double S6_RELATIVE_CUTOFF = 0.50;   // Must be >= 50% of top scorer's S6 damage
            const double S6_ADVANTAGE_RATIO = 3.0;    // Skip if S5 eff is 3x+ their S6 eff
            const int S6_MAX_RESERVED = 10;            // Cap reserved players to avoid draining other stages

            var s6Ranked = playerData
                .Where(p => p.eff.GetValueOrDefault(StageId.S6, 0) >= S6_MIN_FLOOR)
                .OrderByDescending(p => p.eff.GetValueOrDefault(StageId.S6, 0))
                .ToList();

            double topS6Score = s6Ranked.FirstOrDefault().eff?.GetValueOrDefault(StageId.S6, 0) ?? 0;
            double relativeCutoff = topS6Score * S6_RELATIVE_CUTOFF;

            var s6ReservedPlayers = s6Ranked
                .Where(p =>
                {
                    double s6Eff = p.eff.GetValueOrDefault(StageId.S6, 0);
                    double s5Eff = p.eff.GetValueOrDefault(StageId.S5, 0);

                    // Must meet relative cutoff vs top scorer
                    if (s6Eff < relativeCutoff) return false;

                    // Comparative advantage guard: skip if player is far more useful on S5
                    if (s5Eff >= s6Eff * S6_ADVANTAGE_RATIO && s6Eff < topS6Score * 0.75)
                        return false;

                    return true;
                })
                .Take(S6_MAX_RESERVED)
                .ToList();
            
            foreach (var c in s6ReservedPlayers)
            {
                assigned.Add(c.name); // Reserve them, assign later
            }

            // PRIORITY 2: Stage 5 candidates - from remaining players
            var s5Candidates = playerData
                .Where(p => !assigned.Contains(p.name))
                .OrderByDescending(p => p.eff.GetValueOrDefault(StageId.S5, 0))
                .Take(13)
                .ToList();
            
            foreach (var c in s5Candidates)
            {
                stageAssignments[StageId.S5].Add(c.name);
                assigned.Add(c.name);
            }

            // PRIORITY 3: Stage 4 candidates - from remaining players
            var s4Candidates = playerData
                .Where(p => !assigned.Contains(p.name))
                .OrderByDescending(p => p.eff.GetValueOrDefault(StageId.S4, 0))
                .Take(4)
                .ToList();
            
            foreach (var c in s4Candidates)
            {
                stageAssignments[StageId.S4].Add(c.name);
                assigned.Add(c.name);
            }

            // PRIORITY 4: Stage 1-3 group - remaining players (excluding S6 reserved)
            var s123Candidates = playerData
                .Where(p => !assigned.Contains(p.name))
                .ToList();
            
            foreach (var c in s123Candidates)
            {
                stageAssignments[StageId.S1].Add(c.name);
                stageAssignments[StageId.S2].Add(c.name);
                stageAssignments[StageId.S3].Add(c.name);
                assigned.Add(c.name);
            }

            // Pre-check: Will Stage 6 unlock? (Need 400% on S1-4, 500% on S5)
            bool willUnlockStage6 = false;
            if (!todayState.IsStage6Unlocked)
            {
                // Calculate total potential damage on each stage
                var totalDamage = new Dictionary<StageId, double>();
                foreach (var stage in new[] { StageId.S1, StageId.S2, StageId.S3, StageId.S4, StageId.S5 })
                {
                    double total = 0;
                    if (stageAssignments.ContainsKey(stage))
                    {
                        foreach (var playerName in stageAssignments[stage])
                        {
                            var player = playerData.FirstOrDefault(p => p.name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
                            if (player.name != null)
                            {
                                total += player.eff.GetValueOrDefault(stage, 0) * player.attempts;
                            }
                        }
                    }
                    totalDamage[stage] = total;
                }
                
                // Check if we can do 400% on S1-4 and 500% on S5
                bool canClearS1to4 = totalDamage[StageId.S1] >= 400 && 
                                     totalDamage[StageId.S2] >= 400 && 
                                     totalDamage[StageId.S3] >= 400 && 
                                     totalDamage[StageId.S4] >= 400;
                bool canClearS5FiveTimes = totalDamage[StageId.S5] >= 500;
                
                willUnlockStage6 = canClearS1to4 && canClearS5FiveTimes;
            }
            
            // If Stage 6 will unlock, assign the reserved high-power players to it
            if (willUnlockStage6 || todayState.IsStage6Unlocked)
            {
                // Assign ALL reserved S6-capable players (top scorers) to Stage 6
                // This ensures strong S6 players aren't relegated to easier stages
                foreach (var c in s6ReservedPlayers)
                {
                    stageAssignments[StageId.S6].Add(c.name);
                }
            }

            // Simulate battles to calculate clears, final HP, and stage 6 unlock status
            var simulation = SimulateBattles(playerData, stageAssignments, todayState, players);

            plan.ExpectedResets = simulation.TotalResets;
            plan.FinalHpByStage = simulation.FinalHP;
            plan.StageClears = simulation.StageClears;
            plan.AttackLog = simulation.AttackLog;

            // Build stage groups
            foreach (var kvp in stageAssignments.OrderBy(x => (int)x.Key))
            {
                plan.StageGroups.Add(new StageAssignmentGroup
                {
                    Stage = kvp.Key,
                    PlayerNames = kvp.Value.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                });
            }

            return plan;
        }

        /// <summary>
        /// Run a simulation using fixed per-stage per-player attack assignments from the Dispatcher output.
        /// Players with Attacks == 0 use all their remaining attempts on that stage.
        /// </summary>
        public BattlePlanSummary SimulateWithFixedAssignments(
            DispatcherParsedPlan dispatcherPlan,
            List<PlayerStageProfile> players,
            TodayState todayState,
            double marginOfErrorPercent)
        {
            var plan = new BattlePlanSummary();

            // Build effective percentages for each player
            var playerEffMap = new Dictionary<string, Dictionary<StageId, double>>(StringComparer.OrdinalIgnoreCase);
            var playerAvgMap = new Dictionary<string, Dictionary<StageId, double>>(StringComparer.OrdinalIgnoreCase);
            var playerAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in players)
            {
                var attemptsLeft = 3 - p.Attempts.Count(a => a.Day == todayState.CurrentDay);
                if (attemptsLeft <= 0) continue;

                var effMap = new Dictionary<StageId, double>();
                var avgMap = new Dictionary<StageId, double>();
                foreach (var stage in Enum.GetValues<StageId>())
                {
                    p.AveragedPercents.TryGetValue(stage, out var pct);
                    avgMap[stage] = pct;
                    double smartMargin = CalculateSmartMargin(p, stage, marginOfErrorPercent);
                    effMap[stage] = Math.Max(0.0, pct - smartMargin);
                }
                playerEffMap[p.Name] = effMap;
                playerAvgMap[p.Name] = avgMap;
                playerAttempts[p.Name] = attemptsLeft;
            }

            // Build per-stage per-player attack budgets
            // Track how many attacks each player should use on each stage
            var stageBudgets = new Dictionary<StageId, List<(string name, int budget)>>();
            var stageAssignments = new Dictionary<StageId, List<string>>();

            foreach (var stage in Enum.GetValues<StageId>())
            {
                stageBudgets[stage] = new List<(string, int)>();
                stageAssignments[stage] = new List<string>();
            }

            // First pass: allocate explicit budgets
            var totalExplicitBudget = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in dispatcherPlan.StageAssignments)
            {
                foreach (var assignment in kvp.Value)
                {
                    if (assignment.Attacks > 0)
                    {
                        totalExplicitBudget[assignment.PlayerName] =
                            totalExplicitBudget.GetValueOrDefault(assignment.PlayerName, 0) + assignment.Attacks;
                    }
                }
            }

            // Second pass: resolve budgets (0 = all remaining after explicit allocations)
            foreach (var kvp in dispatcherPlan.StageAssignments)
            {
                foreach (var assignment in kvp.Value)
                {
                    int budget;
                    if (assignment.Attacks > 0)
                    {
                        budget = assignment.Attacks;
                    }
                    else
                    {
                        // All remaining attempts
                        int totalAttempts = playerAttempts.GetValueOrDefault(assignment.PlayerName, 3);
                        int explicitlyUsed = totalExplicitBudget.GetValueOrDefault(assignment.PlayerName, 0);
                        budget = Math.Max(0, totalAttempts - explicitlyUsed);
                    }

                    if (budget > 0)
                    {
                        stageBudgets[kvp.Key].Add((assignment.PlayerName, budget));
                        if (!stageAssignments[kvp.Key].Contains(assignment.PlayerName, StringComparer.OrdinalIgnoreCase))
                        {
                            stageAssignments[kvp.Key].Add(assignment.PlayerName);
                        }
                    }
                }
            }

            // Initialize HP
            var hp = new Dictionary<StageId, double>();
            foreach (var stage in Enum.GetValues<StageId>())
            {
                hp[stage] = todayState.RemainingHpByStage.GetValueOrDefault(stage, stage == StageId.S6 && !todayState.IsStage6Unlocked ? 0 : 100);
            }

            var stageClears = Enum.GetValues<StageId>().ToDictionary(s => s, s => 0);
            var attackLog = new List<AttackLogEntry>();
            const int MAX_LOG_ENTRIES = 500;

            var currentCycleStages = new List<StageId> { StageId.S1, StageId.S2, StageId.S3, StageId.S4, StageId.S5 };
            bool stage6Unlocked = todayState.IsStage6Unlocked;
            bool stage6UnlockedThisCycle = false;
            int totalS5Kills = 0;
            int resets = 0;

            if (stage6Unlocked)
            {
                currentCycleStages.Add(StageId.S6);
                // Use the HP from todayState (may be partial if mid-day); only default to 100 if it was 0 (locked)
                if (hp[StageId.S6] <= 0.005)
                    hp[StageId.S6] = 100;
            }

            // Also log players who already attacked today
            var playersWhoAttacked = new List<(string name, int used)>();
            foreach (var player in players)
            {
                int attemptsUsedToday = player.Attempts.Count(a => a.Day == todayState.CurrentDay);
                if (attemptsUsedToday > 0)
                    playersWhoAttacked.Add((player.Name, attemptsUsedToday));
            }

            // Track per-player per-stage remaining attacks
            var remainingBudget = new Dictionary<string, Dictionary<StageId, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in stageBudgets)
            {
                foreach (var (name, budget) in kvp.Value)
                {
                    if (!remainingBudget.ContainsKey(name))
                        remainingBudget[name] = new Dictionary<StageId, int>();
                    remainingBudget[name][kvp.Key] = budget;
                }
            }

            // Build total budget per player for logging
            var totalBudgetPerPlayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var budgetDetailPerPlayer = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in remainingBudget)
            {
                int total = kvp.Value.Values.Sum();
                totalBudgetPerPlayer[kvp.Key] = total;
                var details = kvp.Value.Where(s => s.Value > 0)
                    .OrderBy(s => (int)s.Key)
                    .Select(s => $"S{(int)s.Key}={s.Value}")
                    .ToList();
                budgetDetailPerPlayer[kvp.Key] = details;
            }

            // Global attempts cap for today
            int attemptsAvailable = Math.Max(0, todayState.RemainingHits);
            int attemptsUsed = 0;

            // Log starting state
            attackLog.Add(new AttackLogEntry { PlayerName = "=== DISPATCHER SIMULATION START ===", Stage = StageId.S1, IsReset = true });
            attackLog.Add(new AttackLogEntry { PlayerName = $"Attempts available today: {attemptsAvailable}", Stage = StageId.S1, IsReset = true });

            if (playersWhoAttacked.Any())
            {
                attackLog.Add(new AttackLogEntry { PlayerName = "--- Already Attacked Today ---", Stage = StageId.S1, IsReset = true });
                foreach (var (name, used) in playersWhoAttacked.OrderBy(x => x.name))
                {
                    attackLog.Add(new AttackLogEntry
                    {
                        PlayerName = $"  {name}: {used} used, {totalBudgetPerPlayer.GetValueOrDefault(name, 0)} remaining",
                        Stage = StageId.S1,
                        IsReset = true
                    });
                }
            }

            // Auto-pad players with < 3 attacks by adding to their highest assigned stage,
            // BUT never exceed their remaining attempts for today (3 - used today)
            var shortBudgetPlayers = totalBudgetPerPlayer.Where(kvp => kvp.Value < 3).OrderBy(kvp => kvp.Key).ToList();
            if (shortBudgetPlayers.Any())
            {
                attackLog.Add(new AttackLogEntry { PlayerName = "--- Players With < 3 Attacks (auto-padded) ---", Stage = StageId.S1, IsReset = true });
                foreach (var kvp in shortBudgetPlayers)
                {
                    // Calculate how many attempts the player actually has left today
                    int attemptsLeftForPlayer = playerAttempts.GetValueOrDefault(kvp.Key, 0);
                    int currentBudgetTotal = totalBudgetPerPlayer.GetValueOrDefault(kvp.Key, 0);
                    int missing = Math.Max(0, attemptsLeftForPlayer - currentBudgetTotal);
                    var detail = budgetDetailPerPlayer.GetValueOrDefault(kvp.Key);
                    var detailStr = detail != null ? string.Join(", ", detail) : "none";

                    // Find the highest stage this player is assigned to
                    var playerBudget = remainingBudget[kvp.Key];
                    var highestStage = playerBudget.Keys.OrderByDescending(s => (int)s).First();

                    // Add missing attacks to that stage
                    if (missing > 0)
                    {
                        playerBudget[highestStage] += missing;
                        // Update cached totals used for logging of subsequent players
                        totalBudgetPerPlayer[kvp.Key] = currentBudgetTotal + missing;
                    }

                    // Ensure they're in stageAssignments for that stage
                    if (!stageAssignments[highestStage].Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                        stageAssignments[highestStage].Add(kvp.Key);

                    attackLog.Add(new AttackLogEntry
                    {
                        PlayerName = $"  {kvp.Key}: {currentBudgetTotal} total ({detailStr}) → +{missing} added to S{(int)highestStage}",
                        Stage = StageId.S1,
                        IsReset = true
                    });
                }
            }

            foreach (var stage in currentCycleStages.OrderBy(s => (int)s))
            {
                attackLog.Add(new AttackLogEntry { PlayerName = $"Stage {(int)stage} Starting HP", Stage = stage, RemainingHP = hp[stage], IsReset = true });
            }
            attackLog.Add(new AttackLogEntry { PlayerName = "=== SIMULATED ATTACKS ===", Stage = StageId.S1, IsReset = true });

            int maxIterations = 1000;
            int iteration = 0;

            while (iteration < maxIterations)
            {
                iteration++;

                if (attemptsUsed >= attemptsAvailable)
                    break;

                // Check if all budgets exhausted
                bool allBudgetsUsed = !remainingBudget.Any(p => p.Value.Any(s => s.Value > 0));
                if (allBudgetsUsed) break;

                // Find a stage with HP and available players
                StageId? targetStage = null;
                foreach (var stage in currentCycleStages)
                {
                    if (hp[stage] > 0.005)
                    {
                        bool hasPlayer = stageAssignments[stage].Any(name =>
                            remainingBudget.ContainsKey(name) &&
                            remainingBudget[name].GetValueOrDefault(stage, 0) > 0);

                        if (hasPlayer)
                        {
                            targetStage = stage;
                            break;
                        }
                    }
                }

                // If no stage found, check for reset or try demotion fallback
                if (targetStage == null)
                {
                    bool allCleared = currentCycleStages.All(s => hp[s] <= 0.005);
                    if (allCleared)
                    {
                        resets++;
                        if (attackLog.Count < MAX_LOG_ENTRIES)
                            attackLog.Add(new AttackLogEntry { PlayerName = $"=== RESET #{resets} ===", Stage = StageId.S1, IsReset = true });

                        foreach (var s in currentCycleStages)
                            hp[s] = 100;

                        if (stage6UnlockedThisCycle)
                        {
                            currentCycleStages.Add(StageId.S6);
                            hp[StageId.S6] = 100;
                            stage6UnlockedThisCycle = false;
                        }
                        continue;
                    }

                    // Demotion fallback: find a stuck stage and pull the weakest player from a higher stage
                    // S6 cannot be helped (no higher stage exists)
                    bool demoted = false;
                    foreach (var stuckStage in currentCycleStages.Where(s => hp[s] > 0.005 && s != StageId.S6))
                    {
                        // Search higher stages for a player with remaining budget
                        var higherStages = currentCycleStages
                            .Where(s => (int)s > (int)stuckStage)
                            .OrderBy(s => (int)s)
                            .ToList();

                        // Candidates: players in higher stages with budget remaining who have some damage on the stuck stage
                        var demotionCandidates = new List<(string name, StageId fromStage, double fromEff, double stuckEff)>();

                        foreach (var higherStage in higherStages)
                        {
                            foreach (var playerName in stageAssignments[higherStage])
                            {
                                if (!remainingBudget.ContainsKey(playerName)) continue;
                                if (remainingBudget[playerName].GetValueOrDefault(higherStage, 0) <= 0) continue;

                                double stuckDmg = playerEffMap.GetValueOrDefault(playerName)?.GetValueOrDefault(stuckStage, 0) ?? 0;
                                double stuckAvg = playerAvgMap.GetValueOrDefault(playerName)?.GetValueOrDefault(stuckStage, 0) ?? 0;
                                if (stuckDmg <= 0) stuckDmg = stuckAvg;
                                if (stuckDmg <= 0) continue; // Can't help at all

                                double fromDmg = playerEffMap.GetValueOrDefault(playerName)?.GetValueOrDefault(higherStage, 0) ?? 0;
                                demotionCandidates.Add((playerName, higherStage, fromDmg, stuckDmg));
                            }
                        }

                        if (demotionCandidates.Count > 0)
                        {
                            double stuckHpRemaining = hp[stuckStage];

                            // Prefer candidates who can clear the stuck stage in one hit (damage >= remaining HP)
                            // Apply 0.9 multiplier as conservative variance floor
                            var canClear = demotionCandidates
                                .Where(c => c.stuckEff * 0.9 >= stuckHpRemaining)
                                .ToList();

                            (string name, StageId fromStage, double fromEff, double stuckEff) best;
                            if (canClear.Count > 0)
                            {
                                // Among those who can clear it, pick the one with the lowest power on their original stage
                                // (least impact when removed from that stage)
                                best = canClear.OrderBy(c => c.fromEff).First();
                            }
                            else
                            {
                                // No one can one-shot it; pick whoever deals the most damage on the stuck stage
                                best = demotionCandidates.OrderByDescending(c => c.stuckEff).First();
                            }

                            // Transfer one attack from higher stage to stuck stage
                            remainingBudget[best.name][best.fromStage]--;
                            if (!remainingBudget[best.name].ContainsKey(stuckStage))
                                remainingBudget[best.name][stuckStage] = 0;
                            remainingBudget[best.name][stuckStage]++;

                            // Add to stuck stage assignments if not already there
                            if (!stageAssignments[stuckStage].Contains(best.name, StringComparer.OrdinalIgnoreCase))
                                stageAssignments[stuckStage].Add(best.name);

                            // Log the demotion
                            if (attackLog.Count < MAX_LOG_ENTRIES)
                            {
                                attackLog.Add(new AttackLogEntry
                                {
                                    PlayerName = $">>> DEMOTION: {best.name} moved 1 attack from S{(int)best.fromStage} to S{(int)stuckStage} <<<",
                                    Stage = stuckStage,
                                    IsReset = true
                                });
                            }

                            demoted = true;
                            break; // Re-enter the main loop to process the demoted attack
                        }
                    }

                    if (!demoted)
                        break; // Truly stuck - no demotion possible
                    continue;
                }

                // Pick the first available player for this stage
                string? attackerName = stageAssignments[targetStage.Value]
                    .FirstOrDefault(name =>
                        remainingBudget.ContainsKey(name) &&
                        remainingBudget[name].GetValueOrDefault(targetStage.Value, 0) > 0);

                if (attackerName == null) break;

                var eff = playerEffMap.GetValueOrDefault(attackerName);
                var avg = playerAvgMap.GetValueOrDefault(attackerName);
                double baseDamage = eff?.GetValueOrDefault(targetStage.Value, 0) ?? 0;
                double avgPct = avg?.GetValueOrDefault(targetStage.Value, 0) ?? 0;

                if (baseDamage <= 0)
                    baseDamage = avgPct; // Use avg even if 0; don't inflate with fake 0.5%

                // Variance
                double damage;
                if (baseDamage <= 0)
                {
                    // Player has no data for this stage — log 0% attack
                    damage = 0;
                }
                else
                {
                    double varianceMultiplier;
                    if (avgPct >= 99.0)
                        varianceMultiplier = 0.98 + (_random.NextDouble() * 0.04);
                    else
                        varianceMultiplier = 0.9 + (_random.NextDouble() * 0.2);
                    damage = baseDamage * varianceMultiplier;
                }
                hp[targetStage.Value] = Math.Max(0, hp[targetStage.Value] - damage);
                remainingBudget[attackerName][targetStage.Value]--;

                bool wasCleared = hp[targetStage.Value] <= 0.005;
                if (wasCleared)
                {
                    hp[targetStage.Value] = 0;
                    stageClears[targetStage.Value]++;
                    if (targetStage.Value == StageId.S5)
                    {
                        totalS5Kills++;
                        if (!stage6Unlocked && totalS5Kills >= 5)
                        {
                            stage6Unlocked = true;
                            stage6UnlockedThisCycle = true;
                        }
                    }
                }

                if (attackLog.Count < MAX_LOG_ENTRIES)
                {
                    attackLog.Add(new AttackLogEntry
                    {
                        PlayerName = attackerName,
                        Stage = targetStage.Value,
                        Damage = damage,
                        RemainingHP = hp[targetStage.Value],
                        Cleared = wasCleared,
                        IsReset = false
                    });
                }

                attemptsUsed++;
            }

            // Use any remaining unused attacks on the lowest available stage
            var unusedPlayers = remainingBudget
                .Where(kvp => kvp.Value.Values.Sum() > 0)
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unusedPlayers.Any() && attemptsUsed < attemptsAvailable)
            {
                attackLog.Add(new AttackLogEntry { PlayerName = "=== REMAINING ATTACKS ===", Stage = StageId.S1, IsReset = true });

                foreach (var playerKvp in unusedPlayers)
                {
                    string playerName = playerKvp.Key;
                    int totalRemaining = playerKvp.Value.Values.Sum();

                    while (totalRemaining > 0 && attemptsUsed < attemptsAvailable)
                    {
                        // Find the lowest stage with HP > 0
                        var targetStage = currentCycleStages
                            .Where(s => hp[s] > 0.005)
                            .OrderBy(s => (int)s)
                            .FirstOrDefault();

                        // If no stage has HP, use the lowest stage overall (log as 0% on cleared stage)
                        if (targetStage == default && !currentCycleStages.Any(s => hp[s] > 0.005))
                            targetStage = currentCycleStages.OrderBy(s => (int)s).First();

                        var eff = playerEffMap.GetValueOrDefault(playerName);
                        var avg = playerAvgMap.GetValueOrDefault(playerName);
                        double baseDamage = eff?.GetValueOrDefault(targetStage, 0) ?? 0;
                        double avgPct = avg?.GetValueOrDefault(targetStage, 0) ?? 0;

                        if (baseDamage <= 0)
                            baseDamage = avgPct;

                        double damage;
                        if (baseDamage <= 0 || hp[targetStage] <= 0.005)
                        {
                            damage = 0;
                        }
                        else
                        {
                            double varianceMultiplier = avgPct >= 99.0
                                ? 0.98 + (_random.NextDouble() * 0.04)
                                : 0.9 + (_random.NextDouble() * 0.2);
                            damage = baseDamage * varianceMultiplier;
                        }

                        hp[targetStage] = Math.Max(0, hp[targetStage] - damage);

                        bool wasCleared = hp[targetStage] <= 0.005 && damage > 0;
                        if (wasCleared)
                        {
                            hp[targetStage] = 0;
                            stageClears[targetStage]++;
                        }

                        if (attackLog.Count < MAX_LOG_ENTRIES)
                        {
                            attackLog.Add(new AttackLogEntry
                            {
                                PlayerName = playerName,
                                Stage = targetStage,
                                Damage = damage,
                                RemainingHP = hp[targetStage],
                                Cleared = wasCleared,
                                IsReset = false
                            });
                        }

                        // Decrement from any stage budget this player has
                        var budgetStage = playerKvp.Value.FirstOrDefault(s => s.Value > 0).Key;
                        if (playerKvp.Value.ContainsKey(budgetStage))
                            playerKvp.Value[budgetStage]--;

                        totalRemaining--;
                        attemptsUsed++;
                    }
                }
            }

            plan.ExpectedResets = resets;
            plan.FinalHpByStage = hp;
            plan.StageClears = stageClears;
            plan.AttackLog = attackLog;

            foreach (var kvp in stageAssignments.OrderBy(x => (int)x.Key))
            {
                plan.StageGroups.Add(new StageAssignmentGroup
                {
                    Stage = kvp.Key,
                    PlayerNames = kvp.Value.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                });
            }

            return plan;
        }

        private (int TotalResets, Dictionary<StageId, double> FinalHP, Dictionary<StageId, int> StageClears, List<AttackLogEntry> AttackLog, bool Stage6Unlocked, int AttemptsAvailable) SimulateBattles(
            List<(string name, int attempts, Dictionary<StageId, double> eff, Dictionary<StageId, double> avgPct)> playerData,
            Dictionary<StageId, List<string>> assignments,
            TodayState todayState,
            List<PlayerStageProfile> allPlayers)
        {
            // Initialize HP from current state based on live attack data
            // Keep all HP values as-is from current state
            // Defeated bosses stay at 0% until a reset occurs in the simulation
            var hp = new Dictionary<StageId, double>();
            foreach (var stage in Enum.GetValues<StageId>())
            {
                if (todayState.RemainingHpByStage.TryGetValue(stage, out var currentHp))
                {
                    // Keep the current HP as-is (defeated bosses stay at 0%)
                    hp[stage] = currentHp;
                }
                else
                {
                    // Stage not in current state (e.g., Stage 6 not unlocked yet)
                    hp[stage] = 0;
                }
            }

            // Track clears per stage
            var stageClears = new Dictionary<StageId, int>
            {
                { StageId.S1, 0 },
                { StageId.S2, 0 },
                { StageId.S3, 0 },
                { StageId.S4, 0 },
                { StageId.S5, 0 },
                { StageId.S6, 0 }
            };

            var attackLog = new List<AttackLogEntry>();
            const int MAX_LOG_ENTRIES = 500; // Limit log size

            int totalS5Kills = 0;
            bool stage6Unlocked = todayState.IsStage6Unlocked;
            bool stage6UnlockedThisCycle = false; // Track if S6 was unlocked during current cycle
            int resets = 0;
            
            // Track which stages are part of the CURRENT cycle
            var currentCycleStages = new List<StageId> { StageId.S1, StageId.S2, StageId.S3, StageId.S4, StageId.S5 };
            if (stage6Unlocked)
            {
                currentCycleStages.Add(StageId.S6);
                hp[StageId.S6] = 100;
            }

            // Track attempts used per player
            var attemptsUsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int totalAttemptsAvailable = 0;
            foreach (var p in playerData)
            {
                attemptsUsed[p.name] = 0;
                totalAttemptsAvailable += p.attempts;
            }
            
            // Add header showing starting state
            attackLog.Add(new AttackLogEntry
            {
                PlayerName = "=== SIMULATION START ===",
                Stage = StageId.S1,
                Damage = 0,
                RemainingHP = 0,
                Cleared = false,
                IsReset = true
            });
            
            // List ALL players who already used attempts (check original allPlayers list)
            var playersWhoAttacked = new List<(string name, int used)>();
            foreach (var player in allPlayers)
            {
                int attemptsUsedToday = player.Attempts.Count(a => a.Day == todayState.CurrentDay);
                if (attemptsUsedToday > 0)
                {
                    playersWhoAttacked.Add((player.Name, attemptsUsedToday));
                }
            }
            
            if (playersWhoAttacked.Any())
            {
                foreach (var (name, used) in playersWhoAttacked.OrderBy(x => x.name))
                {
                    attackLog.Add(new AttackLogEntry
                    {
                        PlayerName = $"{name} ({used} used)",
                        Stage = StageId.S1,
                        Damage = 0,
                        RemainingHP = 0,
                        Cleared = false,
                        IsReset = true
                    });
                }
            }
            
            // Show starting HP for each stage
            foreach (var stage in currentCycleStages.OrderBy(s => (int)s))
            {
                attackLog.Add(new AttackLogEntry
                {
                    PlayerName = $"Stage {(int)stage} Starting HP",
                    Stage = stage,
                    Damage = 0,
                    RemainingHP = hp[stage],
                    Cleared = false,
                    IsReset = true
                });
            }
            
            attackLog.Add(new AttackLogEntry
            {
                PlayerName = "=== SIMULATED ATTACKS ===",
                Stage = StageId.S1,
                Damage = 0,
                RemainingHP = 0,
                Cleared = false,
                IsReset = true
            });

            // Simulate until all attempts are exhausted
            int maxIterations = 1000;
            int iteration = 0;
            
            while (iteration < maxIterations)
            {
                iteration++;
                
                // Use current cycle stages for this iteration
                var requiredStages = currentCycleStages;
                
                // Check if all attempts are exhausted first
                bool allAttemptsUsed = playerData.All(p => 
                    attemptsUsed.GetValueOrDefault(p.name, 0) >= p.attempts);
                
                if (allAttemptsUsed)
                {
                    break; // All attempts exhausted, stop simulation
                }
                
                // Find a stage that has HP and has players who can attack it
                StageId? targetStage = null;
                
                foreach (var stage in requiredStages)
                {
                    if (hp[stage] > 0.005)
                    {
                        // Check if any assigned player has attempts left
                        if (assignments.ContainsKey(stage) && assignments[stage].Count > 0)
                        {
                            bool hasAvailablePlayer = assignments[stage].Any(playerName =>
                            {
                                var player = playerData.FirstOrDefault(p => p.name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
                                if (player.name == null) return false;
                                int used = attemptsUsed.GetValueOrDefault(playerName, 0);
                                return used < player.attempts && player.eff.GetValueOrDefault(stage, 0) > 0;
                            });
                            
                            if (hasAvailablePlayer)
                            {
                                targetStage = stage;
                                break;
                            }
                        }
                    }
                }
                
                // Fallback 1: If no stage found with assigned players, find ANY player with eff > 0
                if (targetStage == null)
                {
                    foreach (var stage in requiredStages)
                    {
                        if (hp[stage] > 0.005)
                        {
                            var availablePlayer = playerData
                                .Where(p => attemptsUsed.GetValueOrDefault(p.name, 0) < p.attempts)
                                .Where(p => p.eff.GetValueOrDefault(stage, 0) > 0)
                                .OrderBy(p => p.eff.GetValueOrDefault(StageId.S6, 0))
                                .FirstOrDefault();
                            
                            if (availablePlayer.name != null)
                            {
                                targetStage = stage;
                                if (!assignments[stage].Contains(availablePlayer.name))
                                {
                                    assignments[stage].Add(availablePlayer.name);
                                }
                                break;
                            }
                        }
                    }
                }
                
                // Fallback 2: Force remaining attacks on any stage with HP
                // Every attack is worth points, even if damage is minimal
                if (targetStage == null)
                {
                    foreach (var stage in requiredStages)
                    {
                        if (hp[stage] > 0.005)
                        {
                            var availablePlayer = playerData
                                .Where(p => attemptsUsed.GetValueOrDefault(p.name, 0) < p.attempts)
                                .OrderByDescending(p => p.avgPct.GetValueOrDefault(stage, 0))
                                .FirstOrDefault();
                            
                            if (availablePlayer.name != null)
                            {
                                targetStage = stage;
                                if (!assignments[stage].Contains(availablePlayer.name))
                                {
                                    assignments[stage].Add(availablePlayer.name);
                                }
                                break;
                            }
                        }
                    }
                }
                
                // If all stages cleared, check for reset
                if (targetStage == null)
                {
                    bool allCleared = requiredStages.All(s => hp[s] <= 0.005);
                    if (allCleared)
                    {
                        resets++;
                        
                        // Log the reset
                        if (attackLog.Count < MAX_LOG_ENTRIES)
                        {
                            attackLog.Add(new AttackLogEntry
                            {
                                PlayerName = $"=== RESET #{resets} ===",
                                Stage = StageId.S1,
                                Damage = 0,
                                RemainingHP = 0,
                                Cleared = false,
                                IsReset = true
                            });
                        }
                        
                        // Reset all stages for next cycle
                        foreach (var s in requiredStages)
                        {
                            hp[s] = 100;
                        }
                        
                        // If stage 6 was unlocked during this cycle, add it to the NEXT cycle
                        if (stage6UnlockedThisCycle)
                        {
                            currentCycleStages.Add(StageId.S6);
                            hp[StageId.S6] = 100;
                            stage6UnlockedThisCycle = false; // Reset flag
                        }
                        
                        continue; // Start next cycle
                    }
                    
                    // No attackable stages found but not all attempts used
                    // This shouldn't happen, but if it does, stop to avoid infinite loop
                    break;
                }
                
                bool attackMade = false;
                foreach (var playerName in assignments[targetStage.Value])
                {
                    var player = playerData.FirstOrDefault(p => p.name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
                    if (player.name == null) continue;
                    
                    int used = attemptsUsed.GetValueOrDefault(playerName, 0);
                    if (used >= player.attempts) continue;
                    
                    var baseDamage = player.eff.GetValueOrDefault(targetStage.Value, 0);
                    
                    // If effective damage is 0, use raw averaged percentage or minimum 0.5%
                    // Every attack is worth points even if damage is minimal
                    if (baseDamage <= 0)
                    {
                        baseDamage = Math.Max(0.5, player.avgPct.GetValueOrDefault(targetStage.Value, 0));
                    }
                    
                    // Apply adaptive per-attack variance based on player confidence
                    // High-confidence players (99%+ averaged) get minimal variance to prevent unrealistic failures
                    var avgPct = player.avgPct.GetValueOrDefault(targetStage.Value, 0);
                    double varianceMultiplier;
                    
                    if (avgPct >= 99.0)
                    {
                        // High confidence: ±2% variance (0.98 to 1.02)
                        varianceMultiplier = 0.98 + (_random.NextDouble() * 0.04);
                    }
                    else
                    {
                        // Normal confidence: ±10% variance (0.9 to 1.1)
                        varianceMultiplier = 0.9 + (_random.NextDouble() * 0.2);
                    }
                    
                    var damage = baseDamage * varianceMultiplier;
                    
                    // Make the attack
                    hp[targetStage.Value] = Math.Max(0, hp[targetStage.Value] - damage);
                    attemptsUsed[playerName] = used + 1;
                    attackMade = true;
                    
                    bool wasCleared = hp[targetStage.Value] <= 0.005;
                    if (wasCleared)
                    {
                        hp[targetStage.Value] = 0;
                        stageClears[targetStage.Value]++;
                        
                        if (targetStage.Value == StageId.S5)
                        {
                            totalS5Kills++;
                            if (!stage6Unlocked && totalS5Kills >= 5)
                            {
                                stage6Unlocked = true;
                                stage6UnlockedThisCycle = true; // Mark that S6 was unlocked this cycle
                            }
                        }
                    }
                    
                    // Log the attack
                    if (attackLog.Count < MAX_LOG_ENTRIES)
                    {
                        attackLog.Add(new AttackLogEntry
                        {
                            PlayerName = playerName,
                            Stage = targetStage.Value,
                            Damage = damage,
                            RemainingHP = hp[targetStage.Value],
                            Cleared = wasCleared,
                            IsReset = false
                        });
                    }
                    
                    break; // One attack per iteration
                }
                
                // If no attack was made, stop simulation
                if (!attackMade)
                {
                    // Stage has HP but no assigned players can attack it
                    // Either all attempts exhausted or stage is stuck
                    break;
                }
            }

            return (resets, hp, stageClears, attackLog, stage6Unlocked, totalAttemptsAvailable);
        }
    }
}
