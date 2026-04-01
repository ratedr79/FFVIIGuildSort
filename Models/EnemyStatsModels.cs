using System;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class EnemySearchResult
    {
        public int EnemyId { get; set; }
        public int Level { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SpeciesSummary { get; set; } = string.Empty;
        public IReadOnlyList<string> StageNames { get; set; } = Array.Empty<string>();
        public string StageSummary { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayLevelText { get; set; } = string.Empty;
        public bool IsStageResult { get; set; }
        public string StageName { get; set; } = string.Empty;

        public string Key => $"{EnemyId}:{Level}";
    }

    public sealed class ResistanceEntry
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed class EnemyDetailView
    {
        public int EnemyId { get; set; }
        public int Level { get; set; }
        public string Name { get; set; } = string.Empty;

        public long Hp { get; set; }
        public int PhysicalAttack { get; set; }
        public int MagicalAttack { get; set; }
        public int PhysicalDefense { get; set; }
        public int MagicalDefense { get; set; }

        public IReadOnlyList<string> Species { get; set; } = Array.Empty<string>();
        public IReadOnlyList<ResistanceEntry> ElementResistances { get; set; } = Array.Empty<ResistanceEntry>();
        public IReadOnlyList<string> StatusImmunities { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> BuffDebuffImmunities { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> SkillSummaries { get; set; } = Array.Empty<string>();

        public string Description { get; set; } = string.Empty;
    }
}
