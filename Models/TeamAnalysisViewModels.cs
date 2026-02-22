namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class TeamAnalysisViewModel
    {
        public List<BestTeamResult> RankedTeams { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
}
