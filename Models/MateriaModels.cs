namespace FFVIIEverCrisisAnalyzer.Models
{
    public enum MateriaTier
    {
        Unknown = 0,
        Pot0To7 = 1,
        Pot8To10 = 2,
        Pot11Plus = 3
    }

    public sealed class MateriaOwnership
    {
        public string MateriaName { get; set; } = string.Empty;
        public MateriaTier Tier { get; set; }
        public int Count { get; set; }
    }

    public sealed class MateriaScoreBreakdown
    {
        public string MateriaName { get; set; } = string.Empty;
        public int CountPot0To7 { get; set; }
        public int CountPot8To10 { get; set; }
        public int CountPot11Plus { get; set; }
        public double Score { get; set; }
    }
}
