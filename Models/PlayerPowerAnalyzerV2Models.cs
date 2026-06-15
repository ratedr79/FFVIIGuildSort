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

    // Search depth. Full keeps the exact, byte-identical search behavior: a candidate is only pruned when
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
        // Defaults to Fast: end users want a quick first result, and Fast's reduced caps / epsilon-prune don't
        // change the winner on real inventories. Programmatic/test callers that assert exact recommendations or
        // alternate-team relationships set SearchMode = Full explicitly for the deterministic byte-identical path
        // (e.g. ReproSignatureRegressionTests). The non-slow suite is green on this Fast default.
        public PlayerPowerAnalyzerV2SearchMode SearchMode { get; set; } = PlayerPowerAnalyzerV2SearchMode.Fast;
        public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;
        // 2.7 — per-fight enemy off-element effectiveness. An off-element weapon (doesn't hit the enemy's
        // weakness) loses the weakness-exploit and may be RESISTED by a variable amount that depends on the
        // fight. This factor multiplies such a weapon's effective damage; moderate default 0.5 when unknown.
        public double OffElementDamageFactor { get; set; } = 0.5;
        public List<string> EnabledTeamTemplates { get; set; } = new();

        // GATED SEARCH-WIDTH OVERRIDES (both default null = no behavior change anywhere; the V2 engine uses its
        // mode-based defaults so the interactive page and all repro/regression tests stay byte-identical). Only the
        // offline guild-sort adapter (PowerLevelAnalyzerV2Adapter.BuildRequest) opts in. When non-null they are
        // applied to the AdaptiveSearchProfile: MainSeedTopNOverride widens the per-character distinct-main seeding
        // (N=2 restores inventory-monotonicity), SkeletonExpansionLimitOverride raises the skeleton-cut cap.
        public int? MainSeedTopNOverride { get; set; }
        public int? SkeletonExpansionLimitOverride { get; set; }

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

    // ===== Interactive Team Builder (manual fixed-team scoring) =====

    // One character's chosen loadout in a manually-built team. Items are given by NAME (preferred) or Id; the
    // scorer resolves each to the player's owned copy at its owned overboost/level. Empty slots are allowed.
    public sealed class InteractiveTeamCharacterSpec
    {
        public string Character { get; set; } = string.Empty;
        public string? Main { get; set; }
        public string? Off { get; set; }
        public string? Ultimate { get; set; }
        public string? MainCostume { get; set; }
        public List<string> SubWeapons { get; set; } = new();
        public List<string> SubCostumes { get; set; } = new();
    }

    // A full manual team spec: exactly the 3 character loadouts plus the battle context.
    public sealed class InteractiveTeamSpec
    {
        public List<InteractiveTeamCharacterSpec> Characters { get; set; } = new();
        public Element EnemyWeakness { get; set; } = Element.None;
        public DamageType PreferredDamageType { get; set; } = DamageType.Any;
        public EnemyTargetScenario TargetScenario { get; set; } = EnemyTargetScenario.Unknown;
    }

    // A single active buff/debuff/effect the team provides, surfaced for the UI effect list.
    public sealed class InteractiveTeamEffect
    {
        public string Key { get; set; } = string.Empty;
        // Human-readable, element-aware display label (e.g. "Ice Weapon Boost", "PATK Up", "PDEF Down").
        // The results panel shows this instead of the raw family/key. Falls back to the raw key if empty.
        public string DisplayName { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Axis { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public double? Potency { get; set; }
        public string Kind { get; set; } = string.Empty; // "buff" | "debuff"
    }

    // One of a character's accumulated passive R-ability totals, for the character panel. Points is the character's
    // total accumulated points for the passive (already slot-halved for off-hand/sub contributions by the scorer);
    // Level is the resolved breakpoint level for those points (via the passive's own breakpoint-points array); Value
    // is the resolved display value (e.g. "+40%"). All-Allies passives are kept per-character, labeled "... (All Allies)".
    public sealed class InteractiveTeamCharacterPassive
    {
        public string Label { get; set; } = string.Empty;
        public int Points { get; set; }
        public int Level { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    public sealed class InteractiveTeamCharacterResult
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int Patk { get; set; }
        public int Matk { get; set; }
        public double FinalScore { get; set; }
        public PlayerPowerAnalyzerV2ItemSlot? Main { get; set; }
        public PlayerPowerAnalyzerV2ItemSlot? Off { get; set; }
        public PlayerPowerAnalyzerV2ItemSlot? Ult { get; set; }
        public PlayerPowerAnalyzerV2ItemSlot? Outfit { get; set; }
        public List<PlayerPowerAnalyzerV2ItemSlot> SubWeapons { get; set; } = new();
        public List<PlayerPowerAnalyzerV2ItemSlot> SubOutfits { get; set; } = new();
        // The character's accumulated passive R-ability totals (points/level/value), sorted by Points descending.
        public List<InteractiveTeamCharacterPassive> Passives { get; set; } = new();
    }

    // One row of a passive family's breakpoint chart (the "Show R. Ability Levels" modal): the level, the point
    // threshold to reach it, and the resolved display value. A natural Level-0 / 0pt / "+0%" row leads each chart.
    public sealed class InteractiveTeamRAbilityChartRow
    {
        public int Level { get; set; }
        public int Points { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    // The full breakpoint table for one passive family present on the scored team, straight from the engine's
    // (breakpointPoints[], bonuses[]) arrays — so the chart matches the in-game R. Ability levels exactly.
    public sealed class InteractiveTeamRAbilityChart
    {
        public string Label { get; set; } = string.Empty;
        public List<InteractiveTeamRAbilityChartRow> Rows { get; set; } = new();
    }

    public sealed class InteractiveTeamScoreResult
    {
        public bool HasResult { get; set; }
        public string? FailureReason { get; set; }
        public double Score { get; set; }
        public double RawDamage { get; set; }
        public double Refinement { get; set; }
        public List<InteractiveTeamCharacterResult> Characters { get; set; } = new();
        public List<InteractiveTeamEffect> Buffs { get; set; } = new();
        public List<InteractiveTeamEffect> Debuffs { get; set; } = new();
        public string CopyText { get; set; } = string.Empty;
        // Item NAME/Id values from the spec that could not be resolved to an owned catalog item.
        public List<string> UnresolvedItems { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        // Breakpoint charts (for the "Show R. Ability Levels" modal): one per DISTINCT passive family present across
        // the scored team (the union of the characters' Passives labels), each carrying the full breakpoint table.
        public List<InteractiveTeamRAbilityChart> RAbilityCharts { get; set; } = new();
    }

    // Per-character slot catalog for the page: every weapon/costume option a character can equip, so the
    // client can filter to the player's owned items locally.
    // One resolved passive R-ability on a catalog item, rendered at FULL (main / off-outfit / ultimate slot,
    // slotMultiplier 1.0) and HALF (off-hand / sub-weapon / sub-outfit slot, slotMultiplier 0.5) value. The two
    // strings come straight from the engine's breakpoint resolution + the same point-halving the scorer applies
    // (the half is floor(points * 0.5) BEFORE the breakpoint lookup, mirrored exactly), so they match what
    // ScoreFixedTeam actually credits. Full/Half are short display strings, e.g. "+13%", "+7%", or "Tier 2".
    public sealed class InteractiveTeamBuilderCatalogPassive
    {
        public string Label { get; set; } = string.Empty;
        public string Full { get; set; } = string.Empty;
        public string Half { get; set; } = string.Empty;
        // The raw points the passive contributes in a full-value slot (slotMultiplier 1.0) and a half-value slot
        // (floor(points * 0.5)) — the same point totals feeding Full/Half. Lets the picker show "... · 18 pts · +15%".
        public int FullPoints { get; set; }
        public int HalfPoints { get; set; }
    }

    public sealed class InteractiveTeamBuilderCatalogItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string Slot { get; set; } = string.Empty; // "weapon" | "ultimate" | "costume"
        public string EquipmentType { get; set; } = string.Empty;
        public string Element { get; set; } = string.Empty;
        public string AbilityType { get; set; } = string.Empty;
        public double DamagePercent { get; set; }

        // The item's active ability (weapons/ultimates). For FF7EC the weapon's command ability is identified
        // by the weapon Name; "" for costumes (no active command). AbilityDescription is the rendered ability
        // text when available, else "".
        public string Ability { get; set; } = string.Empty;
        public string AbilityDescription { get; set; } = string.Empty;

        // The item's passive R-abilities, each resolved against its OWN intrinsic points through the engine's
        // breakpoint tables, at full- and half-value slots. Empty when the item has no resolvable passives.
        public List<InteractiveTeamBuilderCatalogPassive> Passives { get; set; } = new();
    }

    public sealed class InteractiveTeamBuilderCatalog
    {
        public List<string> Characters { get; set; } = new();
        public List<InteractiveTeamBuilderCatalogItem> Items { get; set; } = new();
    }
}
