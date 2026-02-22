namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class EnemyAbilitiesConfig
    {
        public List<EnemyAbilityDefinition> EnemyAbilities { get; set; } = new();
    }

    public sealed class EnemyAbilityDefinition
    {
        public string Name { get; set; } = string.Empty;
        public int MaxLevel { get; set; } = 5;
        public double MaxScore { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class EnemyAbilityOwnership
    {
        public string AbilityName { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    public sealed class EnemyAbilityScoreBreakdown
    {
        public string AbilityName { get; set; } = string.Empty;
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public double MaxScore { get; set; }
        public double Score { get; set; }
        public string? Notes { get; set; }
    }
}
