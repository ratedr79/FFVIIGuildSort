using System;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public enum PlayerPowerAnalyzerV2SearchMode
    {
        Adaptive = 0,
        Exhaustive = 1
    }

    public sealed class PlayerPowerAnalyzerV2Request
    {
        public Element EnemyWeakness { get; set; } = Element.None;
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;
        public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;
        public PlayerPowerAnalyzerV2SearchMode SearchMode { get; set; } = PlayerPowerAnalyzerV2SearchMode.Adaptive;
        public List<string> EnabledTeamTemplates { get; set; } = new();
        public List<string> BossImmunityKeys { get; set; } = new();
        public List<string> HardRequiredEffectKeys { get; set; } = new();
        public List<string> SoftPreferredEffectKeys { get; set; } = new();
    }

    public sealed class PlayerPowerAnalyzerV2Result
    {
        public bool HasResult { get; set; }
        public bool IsPlaceholder { get; set; }
        public string? FailureReason { get; set; }
        public string? MatchedTemplateName { get; set; }
        public string? OffensiveAbilitySummary { get; set; }
        public double Score { get; set; }
        public List<string> TeamCharacters { get; set; } = new();
        public List<PlayerPowerAnalyzerV2CharacterBuild> Characters { get; set; } = new();
        public List<PlayerPowerAnalyzerV2AlternateTeam> AlternateTeams { get; set; } = new();
        public List<string> MatchedRequiredEffects { get; set; } = new();
        public List<string> MissingRequiredEffects { get; set; } = new();
        public List<string> MatchedPreferredEffects { get; set; } = new();
        public List<string> MissingPreferredEffects { get; set; } = new();
        public List<string> ProvidedEffectLabels { get; set; } = new();
        public List<string> SuppressedEffectNotes { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> DebugNotes { get; set; } = new();
        public List<PlayerPowerAnalyzerV2ScoreComponent> ScoreBreakdown { get; set; } = new();
        public int AvailableCharacterCount { get; set; }
        public int UnsetLevelWeaponCount { get; set; }
    }

    public sealed class PlayerPowerAnalyzerV2AlternateTeam
    {
        public List<string> Characters { get; set; } = new();
        public double Score { get; set; }
    }

    public sealed class PlayerPowerAnalyzerV2CharacterBuild
    {
        public string CharacterName { get; set; } = string.Empty;
        public CharacterRole Role { get; set; }
        public string CharacterPortraitUrl { get; set; } = string.Empty;
        public double Score { get; set; }
        public int TotalPatk { get; set; }
        public int TotalMatk { get; set; }
        public int TotalHeal { get; set; }
        public List<string> KeyRAbilities { get; set; } = new();
        public List<string> ProvidedEffectLabels { get; set; } = new();
        public List<string> DebugNotes { get; set; } = new();
        public List<PlayerPowerAnalyzerV2ScoreComponent> ScoreBreakdown { get; set; } = new();
        public PlayerPowerAnalyzerV2ItemSlot? MainWeapon { get; set; }
        public PlayerPowerAnalyzerV2ItemSlot? OffHandWeapon { get; set; }
        public PlayerPowerAnalyzerV2ItemSlot? UltimateWeapon { get; set; }
        public List<PlayerPowerAnalyzerV2ItemSlot> SubWeapons { get; set; } = new();
        public PlayerPowerAnalyzerV2ItemSlot? MainOutfit { get; set; }
        public List<PlayerPowerAnalyzerV2ItemSlot> SubOutfits { get; set; } = new();
        public List<PlayerPowerAnalyzerV2MateriaRecommendation> RecommendedMateria { get; set; } = new();
    }

    public sealed class PlayerPowerAnalyzerV2MateriaRecommendation
    {
        public int SlotNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ProvidedEffectLabel { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool IsAssumed { get; set; }
    }

    public sealed class PlayerPowerAnalyzerV2ItemSlot
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SlotName { get; set; } = string.Empty;
        public string EquipmentType { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string PreviewImageUrl { get; set; } = string.Empty;
        public string Element { get; set; } = string.Empty;
        public string AbilityType { get; set; } = string.Empty;
        public string AbilityText { get; set; } = string.Empty;
        public int CommandAtb { get; set; }
        public int InitialChargeTimeSec { get; set; }
        public string UseCount { get; set; } = string.Empty;
        public int OverboostLevel { get; set; }
        public int Level { get; set; }
        public bool IsUltimate { get; set; }
        public double SlotMultiplier { get; set; }
        public int Patk { get; set; }
        public int Matk { get; set; }
        public int Heal { get; set; }
        public double DamagePercent { get; set; }
        public double Score { get; set; }
        public string? SelectedCustomization { get; set; }
        public List<string> PassiveSummaries { get; set; } = new();
        public List<string> ProvidedEffectLabels { get; set; } = new();
        public List<PlayerPowerAnalyzerV2ScoreComponent> ScoreBreakdown { get; set; } = new();
    }

    public sealed class PlayerPowerAnalyzerV2ScoreComponent
    {
        public string Category { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public sealed class PlayerPowerAnalyzerV2EffectOption
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
    }
}
