using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class WeaponSearchItem
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Character { get; init; }
        public required string Element { get; init; }
        public required double DamagePercent { get; init; }
        public required string Range { get; init; }
        public required string AbilityText { get; init; }
        public required string AbilityType { get; init; }
        public required int CommandAtb { get; init; }
        public required string CommandSigil { get; init; }
        public required string EquipmentType { get; init; }
        public required string MateriaSupport0 { get; init; }
        public required string MateriaSupport1 { get; init; }
        public required string MateriaSupport2 { get; init; }
        public required string RechargeTime { get; init; }
        public required string UseCount { get; init; }
        public required List<UpgradeSkillData> UpgradeSkills { get; init; }
        public required List<PassiveSkillTotal> MaxPassiveSkills { get; init; }
        public required string MaxAbilityDescription { get; init; }
        public required List<string> EffectTags { get; init; }
        public required int PatkOb10Lv130 { get; init; }
        public required int MatkOb10Lv130 { get; init; }
        public required int HealOb10Lv130 { get; init; }
        public required List<WeaponCustomization> Customizations { get; init; }
        public required List<SigilInfo> Sigils { get; init; }
    }

    public sealed class UpgradeSkillData
    {
        public required int UpgradeLevel { get; init; }
        public required List<PassiveSkillInfo> PassiveSkills { get; init; }
    }

    public sealed class PassiveSkillInfo
    {
        public required string SkillId { get; init; }
        public required string SkillName { get; init; }
        public required string SkillDescription { get; init; }
        public required int Points { get; init; }
        public required int SkillSlot { get; init; } // 0, 1, or 2
    }

    public sealed class PassiveSkillTotal
    {
        public required string SkillId { get; init; }
        public required string SkillName { get; init; }
        public required int BasePoints { get; init; }
        public required int UpgradePoints { get; init; }
        public required int TotalPoints { get; init; }
        public required int SkillSlot { get; init; }
        public required string SourceLabel { get; init; }
        public bool IsLocked { get; init; }
        public int? LockedUntilLevel { get; init; }
    }

    public sealed class SigilInfo
    {
        public required string SigilType { get; init; }
        public required string SigilSymbol { get; init; }
        public required int Level { get; init; }
        public required string Source { get; init; }
    }

    public sealed class WeaponCustomization
    {
        public required string Slot { get; init; }
        public required string Kind { get; init; }
        public required string Description { get; init; }
    }

    public sealed class WeaponSnapshotResult
    {
        public required string Character { get; init; }
        public required string Name { get; init; }
        public required string EquipmentType { get; init; }
        public required string AbilityText { get; init; }
        public required int Patk { get; init; }
        public required int Matk { get; init; }
        public required int Heal { get; init; }
        public required List<PassiveSkillTotal> RAbilities { get; init; }
        public required List<WeaponCustomization> Customizations { get; init; }
    }
}
