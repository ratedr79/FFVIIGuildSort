namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class GuildRulesConfig
    {
        public int GuildCount { get; set; } = 4;
        public int GuildSize { get; set; } = 30;
        public List<LockedPlayerRule> LockedPlayers { get; set; } = new();
        public List<PlayerGuildExclusionRule> PlayerGuildExclusions { get; set; } = new();
        public List<string> EnsurePlayersPresent { get; set; } = new();
    }

    public sealed class LockedPlayerRule
    {
        public string Player { get; set; } = string.Empty;
        public int Guild { get; set; }
    }

    public sealed class PlayerGuildExclusionRule
    {
        public string Player { get; set; } = string.Empty;
        public List<int> ExcludedGuilds { get; set; } = new();
    }

    public sealed class PlayerGuildAssignment
    {
        public string Player { get; set; } = string.Empty;
        public int Guild { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class GuildAssignmentResult
    {
        public List<PlayerGuildAssignment> Assignments { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
