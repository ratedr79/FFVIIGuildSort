using System;
using System.Collections.Generic;

namespace FFVIIEverCrisisAnalyzer.Models
{
    // Strategy for choosing each character's sub-weapons. Backbone (default) = the legacy heuristic
    // selection scored with the offensive backbone system (ScorePassiveSkill etc.). DamageModelMarginal =
    // pick each sub by its true marginal contribution to EstimateTeamDamage (the multiplicative model).
    // Either way, the selected subs' passive R-abilities are credited at HALF value into the typed team
    // damage headline (Part-1 shared crediting) — the flag only changes WHICH subs are selected.
    public enum PlayerPowerAnalyzerV2SubWeaponSelectionStrategy
    {
        Backbone,
        DamageModelMarginal
    }

    // Search depth. Full (default) keeps the exact, byte-identical search behavior: a candidate is only pruned when
    // its optimistic ceiling cannot beat the current leader. Fast prunes more aggressively (any candidate whose
    // ceiling is within a small epsilon of the leader is skipped), trading a small chance of a near-tie miss for a
    // meaningfully shorter run. The Part-1 tighter ceiling is a valid upper bound in BOTH modes.
    public enum PlayerPowerAnalyzerV2SearchMode
    {
        Full,
        Fast
    }

    public sealed class PlayerPowerAnalyzerV2Request
    {
        public Element EnemyWeakness { get; set; } = Element.None;
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;
        public PlayerPowerAnalyzerV2SubWeaponSelectionStrategy SubWeaponSelectionStrategy { get; set; }
            = PlayerPowerAnalyzerV2SubWeaponSelectionStrategy.Backbone;
        public PlayerPowerAnalyzerV2SearchMode SearchMode { get; set; } = PlayerPowerAnalyzerV2SearchMode.Full;
        public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;
        // 2.7 — per-fight enemy off-element effectiveness. An off-element weapon (doesn't hit the enemy's
        // weakness) loses the weakness-exploit and may be RESISTED by a variable amount that depends on the
        // fight. This factor multiplies such a weapon's effective damage; moderate default 0.5 when unknown.
        public double OffElementDamageFactor { get; set; } = 0.5;
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

        // Full loadouts for this alternate option (so the UI can show its gear, not just the roster).
        public List<PlayerPowerAnalyzerV2CharacterBuild> CharacterBuilds { get; set; } = new();

        // This option's offensive coverage, and the tradeoff vs the recommended team: AddsVsBest are effects
        // this option provides that the best team does not; DropsVsBest are effects the best team has that
        // this option lacks. Together they are the data-driven "why pick this instead" rationale.
        public List<string> ProvidedEffectLabels { get; set; } = new();
        public List<string> AddsVsBest { get; set; } = new();
        public List<string> DropsVsBest { get; set; } = new();

        // This option's own analysis/debug notes (so the UI can show them when the user views this team, the
        // same way result.DebugNotes are shown for the recommended team).
        public List<string> DebugNotes { get; set; } = new();
    }

    public sealed class PlayerPowerAnalyzerV2CharacterBuild
    {
        public string CharacterName { get; set; } = string.Empty;
        public CharacterRole Role { get; set; }
        public CharacterRole EffectiveSubWeaponRole { get; set; }
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
        // Attack range of the weapon's ability ("Single Enemy" / "All Enemies" / etc.). Used by the damage
        // model (2.6) to range-match enemy Damage-Received-Up debuffs to each attacker.
        public string Range { get; set; } = string.Empty;
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
