using FFVIIEverCrisisAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class GuildBattleAssignmentEngine
    {
        private readonly Random _random = new Random();

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
            var playerData = new List<(string name, int attempts, Dictionary<StageId, double> eff)>();
            
            foreach (var p in players)
            {
                var attemptsLeft = 3 - p.Attempts.Count(a => a.Day == todayState.CurrentDay);
                if (attemptsLeft <= 0) continue;
                
                var effMap = new Dictionary<StageId, double>();
                foreach (var stage in Enum.GetValues<StageId>())
                {
                    // Use averaged percentages (mock + live attacks) instead of just mock
                    p.AveragedPercents.TryGetValue(stage, out var pct);
                    
                    // Calculate smart margin based on player's confidence on this and adjacent stages
                    double smartMargin = CalculateSmartMargin(p, stage, marginOfErrorPercent);
                    
                    effMap[stage] = Math.Max(0.0, pct - smartMargin);
                }
                playerData.Add((p.Name, attemptsLeft, effMap));
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
            
            // PRIORITY 1: Reserve Stage 6 capable players (8%+ damage on S6)
            // These are high-power players who should focus on Stage 6
            const double S6_THRESHOLD = 8.0;
            var s6ReservedPlayers = playerData
                .Where(p => p.eff.GetValueOrDefault(StageId.S6, 0) >= S6_THRESHOLD)
                .OrderByDescending(p => p.eff.GetValueOrDefault(StageId.S6, 0))
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
                // Use the reserved S6 players (10%+ damage)
                var s6Candidates = s6ReservedPlayers.Take(6).ToList();
                
                // If we don't have enough reserved players, backfill from S1-3 group
                if (s6Candidates.Count < 6 && stageAssignments[StageId.S1].Count > 0)
                {
                    var additionalS6 = stageAssignments[StageId.S1]
                        .Select(name => playerData.First(p => p.name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        .OrderByDescending(p => p.eff.GetValueOrDefault(StageId.S6, 0))
                        .Take(6 - s6Candidates.Count)
                        .ToList();
                    
                    s6Candidates.AddRange(additionalS6);
                }
                
                foreach (var c in s6Candidates)
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

        private (int TotalResets, Dictionary<StageId, double> FinalHP, Dictionary<StageId, int> StageClears, List<AttackLogEntry> AttackLog, bool Stage6Unlocked) SimulateBattles(
            List<(string name, int attempts, Dictionary<StageId, double> eff)> playerData,
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
            foreach (var p in playerData)
            {
                attemptsUsed[p.name] = 0;
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
                
                // Fallback: If no stage found with assigned players, find ANY player to attack stuck stages
                if (targetStage == null)
                {
                    foreach (var stage in requiredStages)
                    {
                        if (hp[stage] > 0.005)
                        {
                            // Find ANY player with attempts left who can attack this stage
                            // Prioritize lowest S6 power (they're less valuable for S6)
                            var availablePlayer = playerData
                                .Where(p => attemptsUsed.GetValueOrDefault(p.name, 0) < p.attempts)
                                .Where(p => p.eff.GetValueOrDefault(stage, 0) > 0)
                                .OrderBy(p => p.eff.GetValueOrDefault(StageId.S6, 0)) // Lowest S6 power first
                                .FirstOrDefault();
                            
                            if (availablePlayer.name != null)
                            {
                                targetStage = stage;
                                
                                // Temporarily add this player to the stage assignments for this attack
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
                    
                    var damage = player.eff.GetValueOrDefault(targetStage.Value, 0);
                    if (damage <= 0) continue;
                    
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

            return (resets, hp, stageClears, attackLog, stage6Unlocked);
        }
    }
}
