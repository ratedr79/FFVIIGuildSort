using System;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class ShouldIAttackRunEvidence
    {
        public int RunIndex { get; set; }
        public int Seed { get; set; }
        public int Resets { get; set; }
        public int TotalClears { get; set; }
        public double FinalHpSum { get; set; }
        public double TotalEstimatedPoints { get; set; }
        public int EarliestPlayerAttackOrder { get; set; } = int.MaxValue;
        public StageId? EarliestPlayerAttackStage { get; set; }
        public bool UsedDispatcher { get; set; }
        public bool PlayerAttacksWithinHorizon { get; set; }
        public bool FullResetSeen { get; set; }
        public int HorizonAttackCap { get; set; }
        public Dictionary<StageId, double> FinalHpByStage { get; set; } = new();
        public List<AttackLogEntry> AttackLog { get; set; } = new();
    }

    public sealed class ShouldIAttackImmediateRecommendation
    {
        public StageId Stage { get; set; }
        public double ExpectedDamagePercent { get; set; }
        public string Confidence { get; set; } = "Low";
        public bool DiffersFromAssignedExpectation { get; set; }
        public string Rationale { get; set; } = string.Empty;
    }

    public sealed class ShouldIAttackRecommendationResult
    {
        public bool AttackNow { get; set; }
        public string RecommendationLabel => AttackNow ? "Attack now" : "Hold";
        public string Rationale { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
        public int CurrentDay { get; set; }
        public int RemainingHits { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public StageId? AssignedStage { get; set; }
        public bool DispatcherUsed { get; set; }
        public bool FallbackUsed { get; set; }
        public bool ImmediateMode { get; set; }
        public bool StrictFirstHitSatisfied { get; set; }
        public StageId? RunAlignedImmediateStage { get; set; }
        public StageId? HeuristicFallbackImmediateStage { get; set; }
        public bool ImmediateUsedHeuristicFallback { get; set; }
        public ShouldIAttackImmediateRecommendation? ImmediateRecommendation { get; set; }
        public ShouldIAttackRunEvidence? BestRun { get; set; }
        public string SimulatedAttackStagesSummary { get; set; } = "No simulated attacks found.";
        public List<ShouldIAttackRunEvidence> Runs { get; set; } = new();
        public Dictionary<StageId, double> AssumedCurrentHp { get; set; } = new();
    }

    public sealed class ShouldIAttackBulkRow
    {
        public string PlayerName { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string AssignedStage { get; set; } = "-";
        public string Confidence { get; set; } = "Low";
        public bool DispatcherUsed { get; set; }
        public bool FallbackUsed { get; set; }
        public int BestRunResets { get; set; }
        public double BestRunFinalHpSum { get; set; }
        public string Rationale { get; set; } = string.Empty;
    }
}
