namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class MaxDamageReferenceConfiguration
    {
        public int Version { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string LastUpdatedUtc { get; set; } = string.Empty;
        public List<string> Notes { get; set; } = new();
        public List<string> MateriaNotes { get; set; } = new();
        public List<MaxDamageReferenceTeam> Teams { get; set; } = new();
    }

    public sealed class MaxDamageReferenceTeam
    {
        public string Id { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string BattleKey { get; set; } = string.Empty;
        public string Element { get; set; } = string.Empty;
        public bool IsNonElementBattle { get; set; }
        public string PatchLabel { get; set; } = string.Empty;
        public string SeasonLabel { get; set; } = string.Empty;
        public string ArchetypeId { get; set; } = string.Empty;
        public string VariantLabel { get; set; } = string.Empty;
        public int Rank { get; set; }
        public long Score { get; set; }
        public string SourcePlayerAlias { get; set; } = string.Empty;
        public List<string> Notes { get; set; } = new();
        public List<string> StrategyTags { get; set; } = new();
        public string TeamMemoria { get; set; } = string.Empty;
        public List<MaxDamageReferenceCharacter> Characters { get; set; } = new();
    }

    public sealed class MaxDamageReferenceCharacter
    {
        public string CharacterName { get; set; } = string.Empty;
        public string RoleHint { get; set; } = string.Empty;
        public string MainWeapon { get; set; } = string.Empty;
        public string OffHandWeapon { get; set; } = string.Empty;
        public string UltimateWeapon { get; set; } = string.Empty;
        public List<string> SubWeapons { get; set; } = new();
        public string Costume { get; set; } = string.Empty;
        public List<string> SubCostumes { get; set; } = new();
        public List<string> Materia { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    public enum MaxDamageReferenceMateriaRole
    {
        Unknown,
        ElementDamagePhysical,
        ElementDamageMagical,
        ElementDebuffSeed,
        PdefDebuffSeed,
        MdefDebuffSeed,
        Healing,
        SupportBuff,
        StatStickCandidate,
        Utility
    }

    public sealed class MaxDamageReferenceCharacterSummary
    {
        public string CharacterName { get; set; } = string.Empty;
        public CharacterRole Role { get; set; }
        public List<string> Materia { get; set; } = new();
        public List<MaxDamageReferenceMateriaRole> MateriaRoles { get; set; } = new();
        public int ElementDamageMateriaCount { get; set; }
        public int DebuffSeedMateriaCount { get; set; }
        public int StatStickCandidateCount { get; set; }
        public bool HasStatDebuffTierIncreaseSource { get; set; }
        public bool HasDebuffSeedMateria { get; set; }
        public bool HasLikelyTripleElementLoadout { get; set; }
        public bool HasStatStickMateria { get; set; }
        public string MateriaProfileLabel { get; set; } = string.Empty;
    }

    public sealed class MaxDamageReferenceTeamSummary
    {
        public string TeamId { get; set; } = string.Empty;
        public string ArchetypeId { get; set; } = string.Empty;
        public Element EnemyWeakness { get; set; } = Element.None;
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;
        public bool IsNonElementBattle { get; set; }
        public int Rank { get; set; }
        public string TeamMemoria { get; set; } = string.Empty;
        public List<string> CharacterNames { get; set; } = new();
        public string TeamRoleKey { get; set; } = string.Empty;
        public List<MaxDamageReferenceCharacterSummary> Characters { get; set; } = new();
        public bool HasAnyDebuffSeedSetup { get; set; }
        public bool HasSupportOrHealerDebuffSeedSetup { get; set; }
        public bool HasAnyTripleElementDpsLoadout { get; set; }
        public bool HasAnyStatStickMateria { get; set; }
        public bool HasStatDebuffTierIncreaseSource { get; set; }
        public List<string> ProfileNotes { get; set; } = new();
    }

    public sealed class MaxDamageReferenceArchetypeSummary
    {
        public string ArchetypeId { get; set; } = string.Empty;
        public Element EnemyWeakness { get; set; } = Element.None;
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;
        public bool IsNonElementBattle { get; set; }
        public int TeamCount { get; set; }
        public List<string> CharacterNames { get; set; } = new();
        public List<string> CommonMemoria { get; set; } = new();
        public int TeamsWithAnyDebuffSeedSetup { get; set; }
        public int TeamsWithSupportOrHealerDebuffSeedSetup { get; set; }
        public int TeamsWithAnyTripleElementDpsLoadout { get; set; }
        public int TeamsWithAnyStatStickMateria { get; set; }
        public int TeamsWithStatDebuffTierIncreaseSource { get; set; }
        public List<string> Notes { get; set; } = new();
    }

    public sealed class MaxDamageReferenceMatchResult
    {
        public string ArchetypeId { get; set; } = string.Empty;
        public double Score { get; set; }
        public List<string> MatchingSignals { get; set; } = new();
        public List<string> MissingSignals { get; set; } = new();
        public List<string> ReferenceProfileNotes { get; set; } = new();
        public List<string> ReferenceCharacters { get; set; } = new();
    }
}
