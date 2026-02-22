namespace FFVIIEverCrisisAnalyzer.Models
{
    public enum CharacterRole
    {
        DPS,
        Healer,
        Tank,
        Support
    }

    public enum ItemType
    {
        Unknown,
        Outfit,
        Weapon,
        Summon,
        Memoria,
        Materia
    }

    public enum OwnershipType
    {
        NotOwned,
        Owned
    }

    public sealed class AccountRow
    {
        public string InGameName { get; set; } = string.Empty;
        public Dictionary<string, string> ItemResponsesByColumnName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class WeaponOwnership
    {
        public string WeaponName { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public bool IsUltimate { get; set; }
        public int? OverboostLevel { get; set; } // null = not owned, 0..10 = owned
    }

    public sealed class CostumeOwnership
    {
        public string CostumeName { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public OwnershipType Ownership { get; set; }
    }

    public sealed class BestTeamResult
    {
        public string InGameName { get; set; } = string.Empty;
        public double Score { get; set; }
        public int GuildNumber { get; set; }
        public List<string> Characters { get; set; } = new();
        public List<AlternateTeamResult> AlternateTeams { get; set; } = new();
        public Dictionary<string, List<WeaponOwnership>> WeaponsByCharacter { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public TeamScoreBreakdown? Breakdown { get; set; }
    }

    public sealed class AlternateTeamResult
    {
        public List<string> Characters { get; set; } = new();
        public double Score { get; set; }
    }
}
