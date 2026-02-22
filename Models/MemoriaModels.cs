namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class MemoriaConfig
    {
        public List<MemoriaDefinition> Memoria { get; set; } = new();
    }

    public sealed class MemoriaDefinition
    {
        public string Name { get; set; } = string.Empty;
        public int MaxLevel { get; set; } = 10;
        public List<string> Abilities { get; set; } = new();
    }

    public sealed class MemoriaOwnership
    {
        public string MemoriaName { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    public sealed class MemoriaScoreBreakdown
    {
        public string MemoriaName { get; set; } = string.Empty;
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public double Score { get; set; }
        public List<string> Abilities { get; set; } = new();
    }
}
