# Power Analyzer Deep Dive

This section documents the advanced scoring and assignment behavior used by `PowerLevelAnalyzer`.

## Entry Points
- UI page: `Pages/PowerLevelAnalyzer.cshtml`
- Page model: `Pages/PowerLevelAnalyzer.cshtml.cs`
- Analysis orchestrator: `Services/Gb20Analyzer.cs`
- Ingestion: `Services/Gb20Ingestion.cs`
- Core scoring logic: `Services/TeamOptimizer.cs`
- Guild assignment: `Services/GuildAssigner.cs`

## Input Model
- Data source: configured Google Sheet (`GoogleSheets:SurveySheets`) or uploaded CSV.
- Required column: `In-Game Name`.
- Additional used fields include:
  - `Timestamp` (dedupe latest submission)
  - `Discord Name (If different)`
  - `Your Guild`
  - `Your Time Zone`
  - `Battle release day banner?`

## Processing Pipeline
1. Ingest rows to `AccountRow` (`ItemResponsesByColumnName`).
2. Deduplicate by in-game name (latest timestamp wins).
3. Build battle context from user options:
   - enemy weakness
   - preferred damage type
   - target scenario (single/multi)
   - synergy effect bonus overrides
   - enabled team templates
4. Score best team per account using `TeamOptimizer`.
5. Assign players into guilds using `GuildAssigner` + `data/guildRules.json`.
6. Render ranked results, guild summaries, and detailed score breakdowns.

## Synergy Logic and Effect Processing

### Where synergy is applied
- `TeamOptimizer.CalculateSupportSynergyBonus(...)` computes support/synergy contribution from the selected weapons across the 3-character team.
- Per weapon, `SynergyDetection.CalculateSynergyScore(...)` evaluates effect text and returns a weighted synergy score.
- `BattleContext` inputs that control matching:
  - `EnemyWeakness`
  - `PreferredDamageType`
  - `TargetScenario`
  - `SynergyEffectBonusPercents`

### Effect matching model
- Matching is token-driven against `WeaponInfo.EffectTextBlob`.
- Effect detection is context-aware:
  - elemental checks only run when `EnemyWeakness != None`
  - physical/magical checks only run when `PreferredDamageType != Any`
- Several effects are potency-aware:
  - Uses `% Pot` when present (`TryGetEffectPotScaled(...)`) and converts potency to bounded score curves.
  - Falls back to tier mapping (`Low/Mid/High/Extra High`) when direct potency is not available.

### Synergy bonus override behavior
- UI control values are posted via `SynergyEffectBonusPercents[key]` from `PowerLevelAnalyzer`.
- The value is treated as a percent multiplier on that effect's base points:
  - final = `basePoints * (1 + bonusPct/100)`
- If a key is missing or `<= 0`, base points are unchanged.
- Current UI options are `0`, `+100%`, `+200%`, `+300%`, `+400%`, `+500%`.

### Primary effect families scored
- Elemental: resistance down/weakness infliction, elemental damage bonus/up, elemental weapon boost.
- Damage-type: weapon boost, damage bonus, PATK/MATK up, PDEF/MDEF down.
- Damage-received modifiers: single-target, all-target, and generic variants.
- Torpor: short-duration burst vulnerability effect that increases damage taken; scored as a debuff-like synergy, with potency-aware parsing when explicit `% Pot` data exists and tier fallback otherwise.
- Tempo/utility: `ATB+N`, Haste, ATB conservation, Exploit Weakness, Enfeeble.
- Tier amplifiers: applied debuff tier increased, applied buff tier increased, Enliven.
- Amp abilities: parsed by potency/count metadata and bounded before scoring.

### Coverage/range weighting
- After base synergy points are computed, the score is multiplied by a range coverage weight (`GetRangeWeightMultiplier`).
- Buff-like preference: `All Allies` > `Single Ally` > `Self`.
- Debuff-like preference: `All Enemies` > `Single Enemy` (adjusted by single-target vs multi-target scenario).
- Mixed/unknown categories use conservative near-neutral multipliers.

### Team-level dedupe and stacking rules
- Team optimizer groups effects into categories (for example, `elem_res_down:<element>`, `phys_weapon_boost`, `mag_rcvd_up_all`, `haste`, etc.).
- For each category, only the best provider is kept, selected by:
  1. higher coverage weight
  2. then higher synergy score (tie-break)
- ATB+ effects are intentionally allowed to stack and are added separately.
- Final support synergy bonus = deduped category sum + stacked ATB+ contribution.

### Synergy keys exposed in the UI override panel
- `ElementalResistanceDown`
- `AtbPlus`
- `ElementalDamageBonus`
- `ElementalDamageUp`
- `WeaponBoost`
- `ElementalWeaponBoost`
- `SingleTargetDamageReceivedUp`
- `DamageReceivedUp`
- `DamageTypeDamageBonus`
- `DamageTypeAtkUp`
- `ExploitWeakness`
- `Haste`
- `AtbConservationEffect`
- `Enfeeble`
- `Enliven`
- `Torpor`
- `AppliedStatsDebuffTierIncreased`
- `AppliedStatsBuffTierIncreased`
- `AmpAbilities`
- `DefenseDown`

## Advanced Scoring Concepts (Proprietary/Complex)

### Team Construction
- Exactly 3 characters per team.
- Role constrained by configured templates (`TeamTemplateCatalog`).
- Non-template teams can be evaluated with a penalty.

### Role/Weapon Importance
- DPS contribution drives most score impact.
- `MaxDpsAllowed = 2` (hard-coded safeguard in optimizer).
- Weapon scoring includes OB thresholds and potency-aware bonuses.

### Potency and Off-Hand Rules
- Main-hand/Off-hand selection both score, but context-sensitive weighting applies.
- Off-hand potency can be de-weighted or zeroed when it has no relevant battle synergy.
- Element mismatch can zero potency contribution for elemental contexts.

### Utility and Side Systems
- Summons, enemy abilities, memoria, and materia contribute additive utility bonuses.
- Utility list is capped to top contributors in final score.

### Customization Rule (Important)
- Weapon customizations only unlock at `OB1+`.
- At `OB0/5★`, customization badges/details should not be counted in analyzer UI output.

## Guild Assignment Logic
- Loads `GuildRulesConfig` from `data/guildRules.json`.
- Supports:
  - locked players to fixed guild
  - per-player excluded guilds
  - ensure-players list (placeholder if missing from CSV)
- Assignment then fills guilds in ranking order with constraint checks.

## Debugging and Tuning
- Use `Show Debug` to inspect detailed breakdowns and applied rules.
- Confirm template toggles and synergy bonus overrides are reflected in `AppliedRules` output.
- If rankings look off, validate:
  - input column names
  - catalog enrichment status (`DataDiagnostics`)
  - guild rules file validity

## Common Maintenance Tasks
- Update `data/teamTemplates.json` for evolving composition strategies.
- Update `data/guildRules.json` when roster constraints change.
- Adjust synergy mappings in `SynergyDetection` and `TeamOptimizer` for new game effects.
