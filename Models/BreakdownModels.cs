namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class UtilityItemScoreBreakdown
    {
        public string Kind { get; set; } = string.Empty; // Summon / EnemyAbility
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public double Score { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class CostumeScoreBreakdown
    {
        public string CostumeName { get; set; } = string.Empty;
        public string Slot { get; set; } = string.Empty; // Main/Sub
        public double BasePoints { get; set; }
        public int SynergyMatchCount { get; set; }
        public double SynergyPoints { get; set; }
        public double ElementalAbilityPoints { get; set; }
        public double SlotMultiplier { get; set; } = 1.0;
        public double FinalCostumeScore { get; set; }
        public string? SynergyReason { get; set; }
    }

    public sealed class WeaponScoreBreakdown
    {
        public string WeaponName { get; set; } = string.Empty;
        public string? Slot { get; set; }
        public string? SelectionReason { get; set; }
        public int OverboostLevel { get; set; }
        public bool IsUltimate { get; set; }
        public string? SynergyReason { get; set; }
        public double? AbilityPotPercentAtOb10 { get; set; }
        public double? AbilityPotPercentUsed { get; set; }
        public double MultiplyDamageBonusPercent { get; set; }
        public double? EffectiveAbilityPotPercentUsed { get; set; }
        public double PotencyWeightApplied { get; set; } = 1.0;
        public bool WeaknessMatch { get; set; }
        public bool PreferredDamageTypeMatch { get; set; }
        public bool PotencyApplied { get; set; }
        public double BaseOwnedPoints { get; set; }
        public double Ob1Points { get; set; }
        public double Ob6Points { get; set; }
        public double Ob10Points { get; set; }
        public double IntermediateObPoints { get; set; }
        public double UltimateMultiplier { get; set; } = 1.0;
        public double FinalWeaponScore { get; set; }
    }

    public sealed class CharacterScoreBreakdown
    {
        public string CharacterName { get; set; } = string.Empty;
        public CharacterRole Role { get; set; }
        public double RoleWeight { get; set; }
        public List<WeaponScoreBreakdown> ConsideredWeapons { get; set; } = new();
        public List<WeaponScoreBreakdown> SelectedWeapons { get; set; } = new();
        public WeaponScoreBreakdown? UltimateWeapon { get; set; }
        public List<CostumeScoreBreakdown> SelectedCostumes { get; set; } = new();
        public double CostumeScoreSum { get; set; }
        public double RawWeaponScoreSum { get; set; }
        public double BasePlusGearScore { get; set; }
        public double FinalCharacterScore { get; set; }
    }

    public sealed class MissingCatalogItemBreakdown
    {
        public string ColumnName { get; set; } = string.Empty;
        public string RawValue { get; set; } = string.Empty;
        public string InferredKind { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public sealed class TeamScoreBreakdown
    {
        public string InGameName { get; set; } = string.Empty;
        public Element EnemyWeakness { get; set; } = Element.None;
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;
        public int MaxDpsAllowed { get; set; }
        public MemoriaScoreBreakdown? SelectedMemoria { get; set; }
        public double MemoriaScore { get; set; }
        public List<MateriaScoreBreakdown> Materia { get; set; } = new();
        public double MateriaScore { get; set; }
        public List<UtilityItemScoreBreakdown> SelectedUtilityItems { get; set; } = new();
        public double UtilityScore { get; set; }
        public double SynergyBonus { get; set; }
        public List<CharacterScoreBreakdown> Characters { get; set; } = new();
        public double TeamScore { get; set; }
        public List<string> AppliedRules { get; set; } = new();
        public List<MissingCatalogItemBreakdown> MissingCatalogItems { get; set; } = new();
    }
}
