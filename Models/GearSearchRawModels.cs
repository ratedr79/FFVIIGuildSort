using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class WeaponRaw
    {
        public int Id { get; set; }
        public int WeaponType { get; set; }
        public int CharacterId { get; set; }
        public int WeaponEquipmentType { get; set; }
        public int WeaponLevelGroupId { get; set; }
        public int WeaponUpgradeParameterGroupId { get; set; }
        public int WeaponEvolveGroupId { get; set; }
        public int WeaponReleaseSettingGroupId { get; set; }
        public long NameLanguageId { get; set; }
        public int WeaponMateriaSupportId0 { get; set; }
        public int WeaponMateriaSupportId1 { get; set; }
        public int WeaponMateriaSupportId2 { get; set; }
        public int PassiveSkillId0 { get; set; }
        public int PassiveSkillId1 { get; set; }
        public int PassiveSkillId2 { get; set; }
    }

    public sealed class WeaponReleaseSettingRaw
    {
        public int WeaponReleaseSettingGroupId { get; set; }
        public int ReleaseCount { get; set; }
        public int LevelLimit { get; set; }
    }

    public sealed class WeaponLevelRaw
    {
        public int WeaponLevelGroupId { get; set; }
        public int Level { get; set; }
        public int HpCoefficient { get; set; }
        public int PhysicalAttackCoefficient { get; set; }
        public int MagicalCoefficient { get; set; }
        public int PhysicalDefenseCoefficient { get; set; }
        public int MagicalDefenseCoefficient { get; set; }
        public int HealingPowerCoefficient { get; set; }
    }

    public sealed class WeaponUpgradeParameterRaw
    {
        public int WeaponUpgradeParameterGroupId { get; set; }
        public int WeaponUpgradeType { get; set; }
        public int UpgradeCount { get; set; }
        public int HpAddCoefficient { get; set; }
        public int PhysicalAttackAddCoefficient { get; set; }
        public int MagicalAddCoefficient { get; set; }
        public int PhysicalDefenseAddCoefficient { get; set; }
        public int MagicalDefenseAddCoefficient { get; set; }
        public int HealingPowerAddCoefficient { get; set; }
    }

    public sealed class WeaponEvolveRaw
    {
        public int Id { get; set; }
        public int WeaponEvolveGroupId { get; set; }
        public int WeaponEvolveType { get; set; }
    }

    public sealed class WeaponEvolveEffectRaw
    {
        public int Id { get; set; }
        public int WeaponEvolveId { get; set; }
        public int WeaponEvolveEffectType { get; set; }
        public int TargetId { get; set; }
    }

    public sealed class WeaponEvolveWeaponSkillRaw
    {
        public int WeaponEvolveWeaponSkillGroupId { get; set; }
        public int UpgradeCount { get; set; }
        public int WeaponSkillId { get; set; }
    }


    public sealed class CharacterCostumeRaw
    {
        public int Id { get; set; }
        public int CharacterId { get; set; }
        public int PassiveSkillId0 { get; set; }
        public int PassiveSkillId1 { get; set; }
        public int PassiveSkillPoint0 { get; set; }
        public int PassiveSkillPoint1 { get; set; }
        public long NameLanguageId { get; set; }
        public int SkillCharacterCostumeId { get; set; }
    }

    public sealed class SkillCharacterCostumeRaw
    {
        public int Id { get; set; }
        public int SkillActiveId { get; set; }
        public int SkillNotesSetId { get; set; }
    }

    public sealed class CharacterRaw
    {
        public int Id { get; set; }
        public long NameLanguageId { get; set; }
    }

    public sealed class MateriaSupportRaw
    {
        public int Id { get; set; }
        public long NameLanguageId { get; set; }
    }

    public sealed class WeaponUpgradeSkillRaw
    {
        public int WeaponId { get; set; }
        public int WeaponUpgradeType { get; set; }
        public int UpgradeCount { get; set; }
        public int WeaponSkillId { get; set; }
        public int AddPassiveSkillPoint0 { get; set; }
        public int AddPassiveSkillPoint1 { get; set; }
        public int AddPassiveSkillPoint2 { get; set; }
    }

    public sealed class WeaponCustomizationRaw
    {
        public string Slot { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class WeaponRarityRaw
    {
        public int Id { get; set; }
        public int WeaponId { get; set; }
        public int RarityType { get; set; }
        public int WeaponSkillId { get; set; }
        public int BaseHp { get; set; }
        public int BasePhysicalAttack { get; set; }
        public int BaseMagicalAttack { get; set; }
        public int BasePhysicalDefense { get; set; }
        public int BaseMagicalDefense { get; set; }
        public int BaseHealingPower { get; set; }
        public int GrowthBaseHp { get; set; }
        public int GrowthBasePhysicalAttack { get; set; }
        public int GrowthBaseMagicalAttack { get; set; }
        public int GrowthBasePhysicalDefense { get; set; }
        public int GrowthBaseMagicalDefense { get; set; }
        public int GrowthBaseHealingPower { get; set; }
    }

    public sealed class WeaponRarityReleaseSkillRaw
    {
        public int WeaponRarityId { get; set; }
        public int ReleaseCount { get; set; }
        public int AddPassiveSkillPoint0 { get; set; }
        public int AddPassiveSkillPoint1 { get; set; }
        public int AddPassiveSkillPoint2 { get; set; }
    }

    public sealed class SkillWeaponRaw
    {
        public int Id { get; set; }
        public int SkillActiveId { get; set; }
        public int SkillNotesSetId { get; set; }
        public int SkillWeaponType { get; set; }
    }

    public sealed class SkillActiveRaw
    {
        public int Id { get; set; }
        public int SkillBaseId { get; set; }
        public int Cost { get; set; }
        public int UseCountLimit { get; set; }
    }

    public sealed class SkillBaseRaw
    {
        public int Id { get; set; }
        public int BaseAttackType { get; set; }
        public int ElementType { get; set; }
        public int SkillEffectGroupId { get; set; }
        public long NameLanguageId { get; set; }
        public long DescriptionLanguageId { get; set; }
        public int SkillBaseGroupId { get; set; }
    }

    public sealed class SkillEffectGroupEntryRaw
    {
        public long Id { get; set; }
        public int Seq { get; set; }
        public long SkillEffectId { get; set; }
    }

    public sealed class SkillEffectRaw
    {
        public long Id { get; set; }
        public int TargetType { get; set; }
        public int SkillEffectType { get; set; }
        public long SkillEffectDetailId { get; set; }
        public long SkillEffectDescriptionGroupId { get; set; }
        public int TriggerType { get; set; }
        public long TriggerConditionId { get; set; }
    }

    public sealed class SkillDamageEffectRaw
    {
        public long Id { get; set; }
        public int SkillDamageType { get; set; }
        public double MaxDamageCoefficient { get; set; }
    }

    public sealed class SkillAdditionalEffectRaw
    {
        public long Id { get; set; }
        public int SkillAdditionalType { get; set; }
        public bool IsRandom { get; set; }
        public int MinValue { get; set; }
        public int MaxValue { get; set; }
    }

    public sealed class SkillStatusChangeEffectRaw
    {
        public long Id { get; set; }
        public int SkillStatusChangeType { get; set; }
        public int MinDurationSec { get; set; }
        public int MaxDurationSec { get; set; }
        public int MinDuplicationDurationSec { get; set; }
        public int MaxDuplicationDurationSec { get; set; }
        public int EffectCoefficient { get; set; }
        public int EffectCount { get; set; }
    }

    public sealed class SkillStatusConditionEffectRaw
    {
        public long Id { get; set; }
        public int SkillStatusConditionType { get; set; }
        public int MinDurationSec { get; set; }
        public int MaxDurationSec { get; set; }
        public int MinDuplicationDurationSec { get; set; }
        public int MaxDuplicationDurationSec { get; set; }
        public bool IgnoreResist { get; set; }
        public int EffectCoefficient { get; set; }
    }

    public sealed class SkillBuffDebuffRaw
    {
        public long Id { get; set; }
        public int SkillBuffDebuffType { get; set; }
        public int TriggerEffectLevel { get; set; }
        public int TriggerEffectLevelMax { get; set; }
        public int MaxDurationSec { get; set; }
        public int MaxDuplicationDurationSec { get; set; }
    }

    public sealed class SkillBuffDebuffEnhanceRaw
    {
        public long Id { get; set; }
        public int BuffDebuffEnhanceType { get; set; }
        public int EnhanceEffectLevel { get; set; }
        public int EnhanceEffectLevelMax { get; set; }
        public int EnhanceDurationSec { get; set; }
    }

    public sealed class SkillCancelEffectRaw
    {
        public long Id { get; set; }
        public long BuffDebuffGroupId { get; set; }
        public long StatusConditionGroupId { get; set; }
        public long StatusChangeGroupId { get; set; }
    }

    public sealed class SkillAtbChangeEffectRaw
    {
        public long Id { get; set; }
        public int Value { get; set; }
    }

    public sealed class SkillSpecialGaugeChangeEffectRaw
    {
        public long Id { get; set; }
        public int SkillSpecialGaugeChangeType { get; set; }
        public int TargetSkillSpecialType { get; set; }
        public int PermilValue { get; set; }
    }

    public sealed class SkillTacticsGaugeChangeEffectRaw
    {
        public long Id { get; set; }
        public int SkillEffectGaugeChangeType { get; set; }
        public int PermilValue { get; set; }
    }

    public sealed class SkillOveraccelGaugeChangeEffectRaw
    {
        public long Id { get; set; }
        public int PermilValue { get; set; }
    }

    public sealed class SkillCostumeCountChangeEffectRaw
    {
        public long Id { get; set; }
        public int Value { get; set; }
    }

    public sealed class SkillTriggerConditionHpRaw
    {
        public long Id { get; set; }
        public int MinPermil { get; set; }
        public int MaxPermil { get; set; }
    }

    public sealed class SkillLegendaryRaw
    {
        public int Id { get; set; }
        public int InitialChargeTimeSec { get; set; }
        public int RechargeTimeSec { get; set; }
        public int UseCountLimit { get; set; }
    }

    public sealed class SkillNotesSetRaw
    {
        public long Id { get; set; }
        public int SkillNotesId { get; set; }
    }

    public sealed class SkillPassiveRaw
    {
        public int Id { get; set; }
        public long NameLanguageId { get; set; }
    }

    public sealed class SkillPassiveLevelRaw
    {
        public int PassiveSkillId { get; set; }
        public int Level { get; set; }
        public int PassivePoint { get; set; }
    }

    public sealed class SkillEffectDescriptionRaw
    {
        public long Id { get; set; }
        public long DescriptionLanguageId { get; set; }
    }

    public sealed class SkillEffectDescriptionGroupEntryRaw
    {
        public long Id { get; set; }
        public int Seq { get; set; }
        public long SkillEffectDescriptionId { get; set; }
    }

    public sealed class BuffDebuffGroupEntryRaw
    {
        public long Id { get; set; }
        public int SkillBuffDebuffType { get; set; }
    }

    public sealed class StatusConditionGroupEntryRaw
    {
        public long Id { get; set; }
        public int SkillStatusConditionType { get; set; }
    }

    public sealed class StatusChangeGroupEntryRaw
    {
        public long Id { get; set; }
        public int SkillStatusChangeType { get; set; }
    }
}
