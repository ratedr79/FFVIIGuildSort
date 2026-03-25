using System;
using System.Collections.Generic;
using System.Linq;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public enum StageId
    {
        S1 = 1,
        S2 = 2,
        S3 = 3,
        S4 = 4,
        S5 = 5,
        S6 = 6
    }

    public sealed class AttemptLog
    {
        public string PlayerName { get; set; } = string.Empty;
        public int Day { get; set; } // 1..3
        public StageId Stage { get; set; }
        public double Percent { get; set; } // 0..100
        public bool Killed { get; set; }
        public int RowIndex { get; set; } // original CSV row index for ordering within a day
    }

    public sealed class PlayerStageProfile
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<StageId, double> MockPercents { get; set; } = new(); // 0..100 per stage
        public Dictionary<StageId, double> AveragedPercents { get; set; } = new(); // Running average including live attacks
        public List<AttemptLog> Attempts { get; set; } = new();
        public string? Notes { get; set; }
    }

    public sealed class TodayState
    {
        public int CurrentDay { get; set; }
        public int RemainingHits { get; set; }
        public Dictionary<StageId, double> RemainingHpByStage { get; set; } = new(); // 0..100
        public Dictionary<StageId, int> KillsToday { get; set; } = new();
        public Dictionary<StageId, int> KillsPreviousDays { get; set; } = new();
        public bool IsStage6Unlocked { get; set; }
    }

    public sealed class AssignmentRecommendation
    {
        public string PlayerName { get; set; } = string.Empty;
        public StageId TargetStage { get; set; }
        public double EffectivePercent { get; set; }
        public double ExpectedHpRemoved { get; set; }
        public bool ExpectedKill { get; set; }
        public string Rationale { get; set; } = string.Empty;
        public string Confidence { get; set; } = ""; // Green/Yellow/Red
    }

    public sealed class StageAssignmentGroup
    {
        public StageId Stage { get; set; }
        public List<string> PlayerNames { get; set; } = new();
    }

    public sealed class AttackLogEntry
    {
        public string PlayerName { get; set; } = string.Empty;
        public StageId Stage { get; set; }
        public double Damage { get; set; }
        public double RemainingHP { get; set; }
        public bool Cleared { get; set; }
        public bool IsReset { get; set; }
        public double EstimatedPoints { get; set; }
        public double BonusPoints { get; set; }
    }

    public sealed class BattlePlanSummary
    {
        public int ExpectedResets { get; set; }
        public Dictionary<StageId, double> FinalHpByStage { get; set; } = new();
        public Dictionary<StageId, int> StageClears { get; set; } = new();
        public List<StageAssignmentGroup> StageGroups { get; set; } = new();
        public List<AttackLogEntry> AttackLog { get; set; } = new();
    }

    /// <summary>
    /// Represents a player assignment from the Dispatcher application output.
    /// </summary>
    public sealed class DispatcherPlayerAssignment
    {
        public string PlayerName { get; set; } = string.Empty;
        public int Attacks { get; set; } // 0 = use all remaining attempts
    }

    /// <summary>
    /// Parsed Dispatcher output with per-stage player assignments.
    /// </summary>
    public sealed class DispatcherParsedPlan
    {
        public Dictionary<StageId, List<DispatcherPlayerAssignment>> StageAssignments { get; set; } = new();
        public int ExpectedResets { get; set; }
        public string RawOutput { get; set; } = string.Empty;
    }
}
