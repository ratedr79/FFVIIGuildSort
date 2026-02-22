namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class SummonsConfig
    {
        public List<SummonDefinition> Summons { get; set; } = new();
    }

    public sealed class SummonDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Element { get; set; } = "None";
        public List<string> Elements { get; set; } = new();
        public int MaxLevel { get; set; } = 12;
        public string Type { get; set; } = "Single";
    }

    public sealed class SummonOwnership
    {
        public string SummonName { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    public sealed class SummonScoreBreakdown
    {
        public string SummonName { get; set; } = string.Empty;
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public double Score { get; set; }
        public string? Reason { get; set; }
    }
}
