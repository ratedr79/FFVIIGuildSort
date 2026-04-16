using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services;

public sealed class DamageCalcService
{
    private static readonly Dictionary<string, double> DefenseDebuffLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No Debuff"] = 0,
        ["Low"] = 0.15,
        ["Mid"] = 0.25,
        ["High"] = 0.35,
        ["Extra High"] = 0.45,
        ["Extreme High"] = 0.55,
    };

    private static readonly Dictionary<string, double> GenericBuffLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No Buff"] = 0,
        ["Low"] = 0.1,
        ["Mid"] = 0.2,
        ["High"] = 0.3,
        ["Extra High"] = 0.4,
        ["Extreme High"] = 0.5,
        ["Extreme High+"] = 0.6,
        ["Extreme High++"] = 0.7,
    };

    private static readonly Dictionary<string, double> AbilityPotencyLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No Points"] = 0,
        ["Lv. 1 (1 Point)"] = 0.03,
        ["Lv. 2 (5 Points)"] = 0.08,
        ["Lv. 3 (15 Points)"] = 0.15,
        ["Lv. 4 (25 Points)"] = 0.22,
        ["Lv. 5 (35 Points)"] = 0.3,
        ["Lv. 6 (45 Points)"] = 0.35,
        ["Lv. 7 (55 Points)"] = 0.4,
    };

    private static readonly Dictionary<string, double> PhysicalMagicalAbilityPotencyLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No Points"] = 0,
        ["Lv. 1 (1 Point)"] = 0.05,
        ["Lv. 2 (5 Points)"] = 0.15,
        ["Lv. 3 (15 Points)"] = 0.3,
        ["Lv. 4 (25 Points)"] = 0.45,
        ["Lv. 5 (35 Points)"] = 0.6,
        ["Lv. 6 (45 Points)"] = 0.7,
        ["Lv. 7 (55 Points)"] = 0.8,
    };

    private static readonly Dictionary<string, double> ElementalPotencyLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No Points"] = 0,
        ["Lv. 1 (1 Point)"] = 0.06,
        ["Lv. 2 (5 Points)"] = 0.15,
        ["Lv. 3 (15 Points)"] = 0.25,
        ["Lv. 4 (25 Points)"] = 0.4,
        ["Lv. 5 (35 Points)"] = 0.55,
        ["Lv. 6 (45 Points)"] = 0.7,
        ["Lv. 7 (55 Points)"] = 0.85,
        ["Lv. 8 (65 Points)"] = 1,
        ["Lv. 9 (80 Points)"] = 1.1,
        ["Lv. 10 (100 Points)"] = 1.2,
    };

    private static readonly Dictionary<string, double> ElementalPotUpLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No Elem. Pot. Buff"] = 0,
        ["Low"] = 0.1,
        ["Mid"] = 0.25,
        ["High"] = 0.4,
        ["Extra High"] = 0.6,
        ["Extreme High"] = 0.8,
        ["Extreme High+"] = 1,
        ["Extreme High++"] = 1.2,
    };

    private static readonly Dictionary<string, double> OverspeedLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No OS"] = 0,
        ["OS Attacker Lv 1"] = 0,
        ["OS Attacker Lv 2"] = 0.05,
        ["OS Attacker Lv 3"] = 0.1,
        ["OS Attacker Lv 4"] = 0.15,
        ["OS Attacker Lv 5"] = 0.2,
        ["OS Supporter Lv 1"] = 0,
        ["OS Supporter Lv 2"] = 0,
        ["OS Supporter Lv 3"] = 0.05,
        ["OS Supporter Lv 4"] = 0.1,
        ["OS Supporter Lv 5"] = 0.1,
    };

    private static readonly Dictionary<string, double> ElementalResistanceDebuffLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["No Elem. Res. Debuff"] = 0,
        ["Low"] = 0.15,
        ["Mid"] = 0.3,
        ["High"] = 0.5,
        ["Extra High"] = 0.75,
        ["Extreme High"] = 1,
    };

    public static IReadOnlyList<string> AbilityPotencyTierOptions { get; } = AbilityPotencyLookup.Keys.ToList();
    public static IReadOnlyList<string> PhysicalMagicalAbilityPotencyTierOptions { get; } = PhysicalMagicalAbilityPotencyLookup.Keys.ToList();
    public static IReadOnlyList<string> ElementalPotencyTierOptions { get; } = ElementalPotencyLookup.Keys.ToList();
    public static IReadOnlyList<string> ElementalPotUpTierOptions { get; } = ElementalPotUpLookup.Keys.ToList();
    public static IReadOnlyList<string> OverspeedBuffTierOptions { get; } = OverspeedLookup.Keys.ToList();
    public static IReadOnlyList<string> DefenseDebuffTierOptions { get; } = DefenseDebuffLookup.Keys.ToList();
    public static IReadOnlyList<string> GenericBuffTierOptions { get; } = GenericBuffLookup.Keys.ToList();
    public static IReadOnlyList<string> ElementalResistanceDebuffTierOptions { get; } = ElementalResistanceDebuffLookup.Keys.ToList();

    public DamageCalcResult Calculate(DamageCalcRequest request)
    {
        var result = new DamageCalcResult();

        var outfitAbilityBonus = PercentInputToDecimal(request.OutfitAbilityBonus);
        var boostElementalPotAllies = PercentInputToDecimal(request.BoostElementalPotAllies);
        var memoriaElementalPotencyBonus = PercentInputToDecimal(request.MemoriaElementalPotencyBonus);
        var memoriaPhysicalAbilityBonus = PercentInputToDecimal(request.MemoriaPhysicalAbilityBonus);
        var memoriaMagicalAbilityBonus = PercentInputToDecimal(request.MemoriaMagicalAbilityBonus);
        var memoriaSpeciesPotencyBonus = PercentInputToDecimal(request.MemoriaSpeciesPotencyBonus);
        var interruptionSigilDamageBoost = PercentInputToDecimal(request.InterruptionSigilDamageBoost);
        var interruptionRAbilities = PercentInputToDecimal(request.InterruptionRAbilities);
        var interruptionMastery = PercentInputToDecimal(request.InterruptionMastery);
        var interruptionArcanum = PercentInputToDecimal(request.InterruptionArcanum);
        var overspeedPassiveElementalPotency = PercentInputToDecimal(request.OverspeedPassiveElementalPotency);
        var torpor = PercentInputToDecimal(request.Torpor);
        var guildBattleMagAbilityPotency = PercentInputToDecimal(request.GuildBattleMagAbilityPotency);
        var guildBattlePhysAbilityPotency = PercentInputToDecimal(request.GuildBattlePhysAbilityPotency);
        var guildBattleElementalPotency = PercentInputToDecimal(request.GuildBattleElementalPotency);
        var enemyElementalResistanceModifier = PercentInputToDecimal(request.EnemyElementalResistanceModifier);
        var physMagDamageReceivedUp = PercentInputToDecimal(request.PhysMagDamageReceivedUp);
        var elementalDamageReceivedUp = PercentInputToDecimal(request.ElementalDamageReceivedUp);
        var highwindLimitPotencyBonus = PercentInputToDecimal(request.HighwindLimitPotencyBonus);
        var weaponAbilityPotency = PercentInputToDecimal(request.WeaponAbilityPotency);
        var highwindWeaponPotencyBonus = PercentInputToDecimal(request.HighwindWeaponPotencyBonus);
        var sumElementalPotencyFromMateria = PercentInputToDecimal(request.SumElementalPotencyFromMateria);
        var exploitWeaknessBonus = PercentInputToDecimal(request.ExploitWeaknessBonus);
        var physMagWeaponBuffLookup = PercentInputToDecimal(request.PhysMagWeaponBuffTier);
        var elementalWeaponBuffLookup = PercentInputToDecimal(request.ElementalWeaponBuffTier);
        var amplificationPhysicalAbility = PercentInputToDecimal(request.AmplificationPhysicalAbility);
        var elementalAmplification = PercentInputToDecimal(request.ElementalAmplification);
        var physMagBonusAdditionalDamage = PercentInputToDecimal(request.PhysMagBonusAdditionalDamage);
        var elementalBonusAdditionalDamage = PercentInputToDecimal(request.ElementalBonusAdditionalDamage);

        var abilityPotencyLookup = Lookup(AbilityPotencyLookup, request.AbilityPotencyTier);
        var physicalAbilityPotencyLookup = Lookup(PhysicalMagicalAbilityPotencyLookup, request.PhysicalAbilityPotencyTier);
        var magicalAbilityPotencyLookup = Lookup(PhysicalMagicalAbilityPotencyLookup, request.MagicalAbilityPotencyTier);
        var elementalPotencyLookup = Lookup(ElementalPotencyLookup, request.ElementalPotencyTier);
        var pDefDebuff = Lookup(DefenseDebuffLookup, request.PhysicalDefenseDebuffTier);
        var mDefDebuff = Lookup(DefenseDebuffLookup, request.MagicDefenseDebuffTier);
        var elementalPotUpBuffLookup = Lookup(ElementalPotUpLookup, request.ElementalPotUpBuffTier);
        var elementalResDebuffLookup = Lookup(ElementalResistanceDebuffLookup, request.ElementalResistanceDebuffTier);
        var overspeedBuffLookup = Lookup(OverspeedLookup, request.OverspeedBuffTier);
        var physicalAttackBuffLookup = Lookup(GenericBuffLookup, request.PhysicalAttackBuffTier);
        var magicalAttackBuffLookup = Lookup(GenericBuffLookup, request.MagicalAttackBuffTier);

        Validate(request, result);

        var physicalDefenseDenominator = ((request.EnemyPhysicalDefense * (1 - pDefDebuff)) * 2.2) + 100;
        var magicDefenseDenominator = ((request.EnemyMagicDefense * (1 - mDefDebuff)) * 2.2) + 100;

        var physicalAttackBase = request.PhysicalAttackStat * (1 + physicalAttackBuffLookup);
        var magicalAttackBase = request.MagicalAttackStat * (1 + magicalAttackBuffLookup);

        var averageDamagePhysical = request.PhysicalAttackStat
            * 50
            * (weaponAbilityPotency * (1 + highwindWeaponPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + physicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaPhysicalAbilityBonus + request.OutfitPhysCmdStanceBonus / 100d)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + physicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 + amplificationPhysicalAbility + elementalAmplification)
            * (1 + physMagWeaponBuffLookup + elementalWeaponBuffLookup)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattlePhysAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * (1 + physMagDamageReceivedUp)
            * (1 + torpor)
            / physicalDefenseDenominator;

        var averageDamageMagical = request.MagicalAttackStat
            * 50
            * (weaponAbilityPotency * (1 + highwindWeaponPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + magicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaMagicalAbilityBonus + request.OutfitMagCmdStanceBonus / 100d)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + magicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 + amplificationPhysicalAbility + elementalAmplification)
            * (1 + physMagWeaponBuffLookup + elementalWeaponBuffLookup)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattleMagAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * (1 + physMagDamageReceivedUp)
            * (1 + torpor)
            / magicDefenseDenominator;

        var workbookPhysicalPathDamage = averageDamagePhysical;
        var workbookMagicalPathDamage = averageDamageMagical;

        var workbookBranchDamage = request.DamageType.Equals("Magical", StringComparison.OrdinalIgnoreCase)
            ? averageDamageMagical
            : request.DamageType.Equals("Physical", StringComparison.OrdinalIgnoreCase)
                ? averageDamagePhysical
                : (averageDamagePhysical + averageDamageMagical) / 2d;

        var interruptionDamagePhysical = request.PhysicalAttackStat
            * 50
            * (weaponAbilityPotency * (1 + highwindWeaponPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + physicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaPhysicalAbilityBonus + request.OutfitPhysCmdStanceBonus / 100d + interruptionRAbilities + interruptionMastery + interruptionArcanum)
            * (1 + interruptionSigilDamageBoost)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + physicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 + amplificationPhysicalAbility + elementalAmplification)
            * (1 + physMagWeaponBuffLookup + elementalWeaponBuffLookup)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattlePhysAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * (1 + physMagDamageReceivedUp)
            * (1 + torpor)
            / physicalDefenseDenominator;

        var interruptionDamageMagical = request.MagicalAttackStat
            * 50
            * (weaponAbilityPotency * (1 + highwindWeaponPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + magicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaMagicalAbilityBonus + request.OutfitMagCmdStanceBonus / 100d + interruptionRAbilities + interruptionMastery + interruptionArcanum)
            * (1 + interruptionSigilDamageBoost)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + magicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 + amplificationPhysicalAbility + elementalAmplification)
            * (1 + physMagWeaponBuffLookup + elementalWeaponBuffLookup)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattleMagAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * (1 + physMagDamageReceivedUp)
            * (1 + torpor)
            / magicDefenseDenominator;

        var interruptionBranchDamage = request.DamageType.Equals("Magical", StringComparison.OrdinalIgnoreCase)
            ? interruptionDamageMagical
            : request.DamageType.Equals("Physical", StringComparison.OrdinalIgnoreCase)
                ? interruptionDamagePhysical
                : interruptionDamagePhysical;

        var summonType = request.SummonLimitBreakDamageType;
        var limitComboMultiplier = LookupLimitComboMultiplier(request.LimitComboTier);
        var summonSkillPotency = PercentInputToDecimal(request.SkillPotencyValue);

        var summonPhysicalDamage = request.PhysicalAttackStat
            * 50
            * (summonSkillPotency * (1 + highwindLimitPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + physicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaPhysicalAbilityBonus + request.OutfitPhysCmdStanceBonus / 100d)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + physicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattlePhysAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * limitComboMultiplier
            / physicalDefenseDenominator;

        var summonMagicalDamage = request.MagicalAttackStat
            * 50
            * (summonSkillPotency * (1 + highwindLimitPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + magicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaMagicalAbilityBonus + request.OutfitMagCmdStanceBonus / 100d)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + magicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattleMagAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * limitComboMultiplier
            / magicDefenseDenominator;

        var mixedAttackAverage = (request.PhysicalAttackStat + request.MagicalAttackStat) / 2d;
        var mixedPhysicalDenominator = (Math.Ceiling(((request.EnemyPhysicalDefense + request.EnemyMagicDefense) / 2d) * (1 - pDefDebuff)) * 2.2) + 100;
        var mixedMagicalDenominator = (Math.Ceiling(((request.EnemyPhysicalDefense + request.EnemyMagicDefense) / 2d) * (1 - mDefDebuff)) * 2.2) + 100;

        var mixedSummonPhysical = mixedAttackAverage
            * 50
            * (summonSkillPotency * (1 + highwindLimitPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + physicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaPhysicalAbilityBonus + request.OutfitPhysCmdStanceBonus / 100d)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + physicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattlePhysAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * limitComboMultiplier
            / mixedPhysicalDenominator;

        var mixedSummonMagical = mixedAttackAverage
            * 50
            * (summonSkillPotency * (1 + highwindLimitPotencyBonus) * 1.5)
            * (1 + abilityPotencyLookup + magicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaMagicalAbilityBonus + request.OutfitMagCmdStanceBonus / 100d)
            * (1 + overspeedBuffLookup)
            * (1 + sumElementalPotencyFromMateria)
            * (1 + magicalAttackBuffLookup)
            * (1 + elementalPotUpBuffLookup)
            * (1 + overspeedPassiveElementalPotency)
            * (1 + elementalResDebuffLookup)
            * (1 + exploitWeaknessBonus)
            * (1 - enemyElementalResistanceModifier)
            * (1 + guildBattleMagAbilityPotency + guildBattleElementalPotency)
            * (1 + memoriaSpeciesPotencyBonus)
            * limitComboMultiplier
            / mixedMagicalDenominator;

        var summonLimitBreakDamage = summonType.Equals("Magical", StringComparison.OrdinalIgnoreCase)
            ? summonMagicalDamage
            : summonType.Equals("Physical", StringComparison.OrdinalIgnoreCase)
                ? summonPhysicalDamage
                : (mixedSummonPhysical + mixedSummonMagical) / 2d;

        var additionalBonusDamage = workbookBranchDamage * (physMagBonusAdditionalDamage + elementalBonusAdditionalDamage);
        var totalDamage = workbookBranchDamage + additionalBonusDamage;

        result.AbilityPotencyLookup = abilityPotencyLookup;
        result.PhysicalAbilityPotencyLookup = physicalAbilityPotencyLookup;
        result.MagicalAbilityPotencyLookup = magicalAbilityPotencyLookup;
        result.ElementalPotencyLookup = elementalPotencyLookup;
        result.PhysicalPotencyStackMultiplier = 1 + abilityPotencyLookup + physicalAbilityPotencyLookup + elementalPotencyLookup + outfitAbilityBonus + boostElementalPotAllies + memoriaElementalPotencyBonus + memoriaPhysicalAbilityBonus;
        result.PhysicalDefenseDebuffLookup = pDefDebuff;
        result.MagicDefenseDebuffLookup = mDefDebuff;
        result.PhysicalDefenseDenominator = physicalDefenseDenominator;
        result.MagicDefenseDenominator = magicDefenseDenominator;
        result.WorkbookPhysicalPathDamage = workbookPhysicalPathDamage;
        result.WorkbookMagicalPathDamage = workbookMagicalPathDamage;
        result.WorkbookBranchDamage = workbookBranchDamage;
        result.SummonLimitBreakDamage = summonLimitBreakDamage;
        result.AverageLbSummonDamage = summonLimitBreakDamage;
        result.AverageDamage = workbookBranchDamage;
        result.AdditionalBonusDamage = additionalBonusDamage;
        result.TotalDamage = totalDamage;
        result.DamageRange = FormatRange(workbookBranchDamage);
        result.InterruptionPhaseAverageDamage = interruptionBranchDamage;
        result.InterruptionPhaseDamageRange = FormatRange(interruptionBranchDamage);
        result.EffectiveAttack = request.DamageType.Equals("Magical", StringComparison.OrdinalIgnoreCase) ? magicalAttackBase : physicalAttackBase;
        result.PotencyMultiplier = weaponAbilityPotency * (1 + highwindWeaponPotencyBonus);
        result.TotalMultiplier = 1;
        result.ResistanceMultiplier = (1 - enemyElementalResistanceModifier) * (1 + elementalResDebuffLookup);
        result.PreCritDamage = workbookBranchDamage;
        result.IsCriticalApplied = request.IsCritical;
        result.FinalDamage = request.IsCritical
            ? workbookBranchDamage * (1.5 + (request.CriticalDamageBonusPercent / 100d))
            : workbookBranchDamage;
        result.SigilDamageBoostLookupUsed = interruptionSigilDamageBoost;
        result.OverspeedBuffLookupUsed = overspeedBuffLookup;
        result.InterruptionMasteryPhysicalLookupUsed = interruptionMastery;
        result.InterruptionMasteryMagicalLookupUsed = interruptionMastery;
        result.ElementalPotUpLookupUsed = elementalPotUpBuffLookup;
        result.ElementalResistanceDebuffLookupUsed = elementalResDebuffLookup;

        return result;
    }

    private static double Lookup(IReadOnlyDictionary<string, double> table, string key)
    {
        return table.TryGetValue(key, out var value) ? value : 0;
    }

    private static string FormatRange(double value)
    {
        var min = Math.Floor(value * 0.9874375d);
        var max = Math.Floor(value * 1.015625d);
        return $"{min:N0}~{max:N0}";
    }

    private static double PercentInputToDecimal(double value)
    {
        return value / 100d;
    }

    private static double LookupLimitComboMultiplier(string tier)
    {
        return tier switch
        {
            "100%" => 1,
            "110%" => 1.1,
            "125%" => 1.25,
            _ => 1
        };
    }

    private static void Validate(DamageCalcRequest request, DamageCalcResult result)
    {
        if (request.InterruptionSigilDamageBoost < 0)
        {
            result.ValidationWarnings.Add("Interruption Sigil Damage Boost should be >= 0.");
            result.FieldWarnings.Add(new DamageCalcValidationWarning
            {
                Field = nameof(request.InterruptionSigilDamageBoost),
                Message = "Value is below expected workbook range."
            });
        }

        if (request.DamageType.Equals("Magical", StringComparison.OrdinalIgnoreCase) && request.MagicalAttackStat <= 0)
        {
            result.ValidationWarnings.Add("Damage Type is Magical but M.Atk is 0.");
            result.FieldWarnings.Add(new DamageCalcValidationWarning
            {
                Field = nameof(request.MagicalAttackStat),
                Message = "M.Atk should be > 0 for magical damage type."
            });
        }

        if (request.EnemyPhysicalDefense <= 0)
        {
            result.ValidationWarnings.Add("Enemy PDEF should be > 0.");
            result.FieldWarnings.Add(new DamageCalcValidationWarning
            {
                Field = nameof(request.EnemyPhysicalDefense),
                Message = "Enemy PDEF is out of expected range."
            });
        }

        if (request.EnemyMagicDefense <= 0)
        {
            result.ValidationWarnings.Add("Enemy MDEF should be > 0.");
            result.FieldWarnings.Add(new DamageCalcValidationWarning
            {
                Field = nameof(request.EnemyMagicDefense),
                Message = "Enemy MDEF is out of expected range."
            });
        }
    }
}
