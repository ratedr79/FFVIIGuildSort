using System;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class SimulationTestSettings
    {
        public int NumberOfRuns { get; set; } = 10;
        public int CurrentDay { get; set; } = 1;
        public double MarginOfErrorPercent { get; set; } = 4.0;
        public string SeedMode { get; set; } = "Auto"; // Auto, Fixed, Incremental
        public int FixedSeed { get; set; } = 42;
        public bool EnableVariance { get; set; } = true;
        public bool EnableOutlierFilter { get; set; } = true;
        public bool EnableDeviationCap { get; set; } = true;
    }

    public sealed class SingleRunResult
    {
        public int RunIndex { get; set; }
        public int Seed { get; set; }
        public int Resets { get; set; }
        public Dictionary<StageId, double> FinalHP { get; set; } = new();
        public Dictionary<StageId, int> StageClears { get; set; } = new();
        public bool Stage6Unlocked { get; set; }
        public Dictionary<StageId, List<string>> Assignments { get; set; } = new();
        public List<AttackLogEntry> AttackLog { get; set; } = new();
        public int TotalAttacks { get; set; }
        public int AttemptsAvailable { get; set; }
        public int AttemptsUsed { get; set; }
    }

    public sealed class PlayerAssignmentRationale
    {
        public string PlayerName { get; set; } = string.Empty;
        public Dictionary<StageId, double> AveragedPercents { get; set; } = new();
        public Dictionary<StageId, double> EffectivePercents { get; set; } = new();
        public Dictionary<StageId, double> SmartMargins { get; set; } = new();
        public string AssignedStage { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool IsS6Reserved { get; set; }
    }

    public sealed class AggregatedTestResults
    {
        public int TotalRuns { get; set; }
        public SimulationTestSettings Settings { get; set; } = new();

        // Summary stats
        public double AvgResets { get; set; }
        public double MinResets { get; set; }
        public double MaxResets { get; set; }
        public double P10Resets { get; set; }
        public double P90Resets { get; set; }

        public Dictionary<StageId, double> AvgFinalHP { get; set; } = new();
        public Dictionary<StageId, double> AvgClears { get; set; } = new();
        public Dictionary<StageId, double> ClearRatePercent { get; set; } = new(); // % of runs where stage was cleared at least once
        public double Stage6UnlockRatePercent { get; set; }

        // Assignment frequency: Player -> Stage -> count across runs
        public Dictionary<string, Dictionary<StageId, int>> AssignmentFrequency { get; set; } = new();
        public int TotalPlayers { get; set; }

        // Variance impact
        public int VarianceAffectedClears { get; set; } // clears that wouldn't happen without variance
        public int VarianceAffectedFails { get; set; } // fails that would clear without variance
        public int TotalSimulatedAttacks { get; set; }

        // Best/Worst run indices (1-based) based on scoring metric
        public int BestRunIndex { get; set; }
        public int WorstRunIndex { get; set; }

        // Individual run results for drill-down
        public List<SingleRunResult> Runs { get; set; } = new();

        // Rationale (from single detailed run)
        public List<PlayerAssignmentRationale> Rationales { get; set; } = new();
    }
}
