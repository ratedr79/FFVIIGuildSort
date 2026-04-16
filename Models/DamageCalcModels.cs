namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class DamageCalcValidationWarning
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class DamageCalcRequest
    {
        public double PhysicalAttackStat { get; set; } = 3715;
        public double MagicalAttackStat { get; set; }
        public double WeaponAbilityPotency { get; set; }
        public double HighwindWeaponPotencyBonus { get; set; }
        public double SumElementalPotencyFromMateria { get; set; }
        public string DamageType { get; set; } = "Physical/Magical";

        public string AbilityPotencyTier { get; set; } = "No Points";
        public string PhysicalAbilityPotencyTier { get; set; } = "Lv. 5 (35 Points)";
        public string MagicalAbilityPotencyTier { get; set; } = "No Points";
        public string ElementalPotencyTier { get; set; } = "No Points";

        public string PhysicalAttackBuffTier { get; set; } = "No Buff";
        public string MagicalAttackBuffTier { get; set; } = "No Buff";
        public string ElementalPotUpBuffTier { get; set; } = "No Elem. Pot. Buff";
        public double ExploitWeaknessBonus { get; set; }
        public double PhysMagWeaponBuffTier { get; set; }
        public double ElementalWeaponBuffTier { get; set; }
        public double AmplificationPhysicalAbility { get; set; }
        public double ElementalAmplification { get; set; }
        public double PhysMagBonusAdditionalDamage { get; set; }
        public double ElementalBonusAdditionalDamage { get; set; }

        public double OutfitAbilityBonus { get; set; } = 30;
        public double OutfitPhysCmdStanceBonus { get; set; }
        public double OutfitMagCmdStanceBonus { get; set; }
        public double BoostElementalPotAllies { get; set; }

        public double MemoriaElementalPotencyBonus { get; set; } = 15;
        public double MemoriaPhysicalAbilityBonus { get; set; } = 15;
        public double MemoriaMagicalAbilityBonus { get; set; }
        public double MemoriaSpeciesPotencyBonus { get; set; }

        public double InterruptionSigilDamageBoost { get; set; }
        public double InterruptionRAbilities { get; set; }
        public double InterruptionMastery { get; set; }
        public double InterruptionArcanum { get; set; }

        public double OverspeedPassiveElementalPotency { get; set; }
        public string OverspeedBuffTier { get; set; } = "No OS";

        public double GuildBattleMagAbilityPotency { get; set; }
        public double GuildBattlePhysAbilityPotency { get; set; }
        public double GuildBattleElementalPotency { get; set; }

        public double EnemyPhysicalDefense { get; set; } = 100;
        public double EnemyMagicDefense { get; set; } = 100;
        public string PhysicalDefenseDebuffTier { get; set; } = "No Debuff";
        public string MagicDefenseDebuffTier { get; set; } = "No Debuff";
        public double EnemyElementalResistanceModifier { get; set; } = -200;
        public string ElementalResistanceDebuffTier { get; set; } = "No Elem. Res. Debuff";
        public double Torpor { get; set; }
        public double PhysMagDamageReceivedUp { get; set; }
        public double ElementalDamageReceivedUp { get; set; }
        public double EnemyPhysicalDefenseAfterMulti { get; set; }
        public double EnemyMagicDefenseAfterMulti { get; set; }

        public string SummonLimitBreakDamageType { get; set; } = "Physical/Magical";
        public double HighwindLimitPotencyBonus { get; set; } = 31;
        public double SkillPotencyValue { get; set; } = 75;
        public string LimitComboTier { get; set; } = "110%";

        public double AdditionalBonusDamageMultiplier { get; set; } = 1.1;

        public double AttackStat { get; set; } = 9000;
        public double PotencyPercent { get; set; } = 480;
        public double SkillMultiplierPercent { get; set; }
        public double ElementalBonusPercent { get; set; }
        public double StanceBonusPercent { get; set; }
        public double OtherMultiplierPercent { get; set; }
        public double EnemyDefense { get; set; }
        public double EnemyResistancePercent { get; set; }
        public bool IsCritical { get; set; }
        public double CriticalDamageBonusPercent { get; set; }
        public double SigilDamageBoostLookup { get; set; } = 0.1;
        public double OverspeedBuffLookup { get; set; }
        public double InterruptionMasteryLookupPhysical { get; set; }
        public double InterruptionMasteryLookupMagical { get; set; }
        public double GuildBattleAbilityPotencyLookup { get; set; }
        public double ElementalPotUpBuff { get; set; }
        public double ElementalResistanceDebuffLookup { get; set; }
        public double InterruptionMasteryBonus { get; set; }
        public double InterruptionArcanumBonus { get; set; }
    }

    public sealed class DamageCalcResult
    {
        public double EffectiveAttack { get; set; }
        public double PotencyMultiplier { get; set; }
        public double TotalMultiplier { get; set; }
        public double ResistanceMultiplier { get; set; }
        public double PreCritDamage { get; set; }
        public double FinalDamage { get; set; }
        public bool IsCriticalApplied { get; set; }
        public double AbilityPotencyLookup { get; set; }
        public double PhysicalAbilityPotencyLookup { get; set; }
        public double MagicalAbilityPotencyLookup { get; set; }
        public double ElementalPotencyLookup { get; set; }
        public double PhysicalPotencyStackMultiplier { get; set; }
        public double PhysicalDefenseDebuffLookup { get; set; }
        public double MagicDefenseDebuffLookup { get; set; }
        public double PhysicalDefenseDenominator { get; set; }
        public double MagicDefenseDenominator { get; set; }
        public double WorkbookPhysicalPathDamage { get; set; }
        public double WorkbookMagicalPathDamage { get; set; }
        public double WorkbookBranchDamage { get; set; }
        public double SummonLimitBreakDamage { get; set; }
        public double AverageLbSummonDamage { get; set; }
        public string DamageRange { get; set; } = string.Empty;
        public double AverageDamage { get; set; }
        public double AdditionalBonusDamage { get; set; }
        public double TotalDamage { get; set; }
        public string InterruptionPhaseDamageRange { get; set; } = string.Empty;
        public double InterruptionPhaseAverageDamage { get; set; }
        public double SigilDamageBoostLookupUsed { get; set; }
        public double OverspeedBuffLookupUsed { get; set; }
        public double InterruptionMasteryPhysicalLookupUsed { get; set; }
        public double InterruptionMasteryMagicalLookupUsed { get; set; }
        public double ElementalPotUpLookupUsed { get; set; }
        public double ElementalResistanceDebuffLookupUsed { get; set; }
        public List<DamageCalcValidationWarning> FieldWarnings { get; set; } = new();
        public List<string> ValidationWarnings { get; set; } = new();
        public string FormulaVersion { get; set; } = "excel-dmgcalc-v1";
        public string Notes { get; set; } = string.Empty;
    }
}
