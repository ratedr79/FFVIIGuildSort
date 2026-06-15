# Power Analyzer Deep Dive

This section documents the advanced scoring and assignment behavior used by `PowerLevelAnalyzer`.

## Entry Points
- UI page: `Pages/PowerLevelAnalyzer.cshtml`
- Page model: `Pages/PowerLevelAnalyzer.cshtml.cs`
- Analysis orchestrator: `Services/Gb20Analyzer.cs`
- Ingestion: `Services/Gb20Ingestion.cs`
- Core scoring logic: `Services/TeamOptimizer.cs`
- Guild assignment: `Services/GuildAssigner.cs`

## Request Execution (Async Jobs)
A full-player-base run can take minutes, so the page does not analyze inside the POST. The "Find Best Teams" button starts a background job (`?handler=StartAsync` → `AnalysisJobService`), the client polls `?handler=AnalyzeStatus` behind an elapsed-time overlay, and on completion it redirects to `?resultJobId=` which renders the precomputed result. This keeps every request sub-second (avoids Cloudflare's 100s `524`). The heavy logic lives in `ComputeAsync(byte[])`; the job runs it on a fresh page-model instance and returns a `PowerLevelAnalysisResult` bundle. The synchronous `OnPostAsync` remains a no-JS fallback. See [Application Overview](../architecture/application-overview.md#async-background-analysis-jobs) and [maintenance notes](../notes/special-cases-and-maintenance.md#async-analysis-job-notes) (incl. the still-synchronous CSV export caveat).

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
- Costume scoring also uses weighted matching via `SynergyDetection.CalculateSynergyMatchScore(...)`, with debug reasons produced by `DescribeWeightedSynergyMatches(...)`.
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
- Team optimizer groups effects into categories (for example, `elem_res_down:<element>`, `phys_weapon_boost`, `mag_rcvd_up_all`, `haste`, `enliven`, `torpor`, `amp_abilities`, etc.).
- Category weights are centralized in `SynergyDetection` so matching, scoring, and debug labels stay aligned.
- For each category, only the best provider is kept, selected by:
  1. higher weighted category value
  2. then higher coverage weight
  3. then higher synergy score (tie-break)
- Team bonus applies diminishing returns as categories stack, so broad reliable support wins over raw weak-effect volume.
- Team synergy bonus is computed from the final selected weapons after any re-optimization pass, not from stale pre-swap choices.
- Debug output now stores a per-category weighted-note trail in `TeamScoreBreakdown.SynergyNotes`.

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
- Weapon scoring includes OB thresholds plus separate damage, effect, passive, customization, and reliability sub-scores.

### Potency and Off-Hand Rules
- DPS main-hand is selected primarily for teased-context damage output.
- DPS off-hand is selected as a utility-biased secondary weapon: selection prefers synergy/coverage value first, then falls back to the off-hand score as a tie-break.
- DPS off-hand direct damage weight is reduced relative to main-hand:
  - `0.60` when both teased weakness and preferred damage type match
  - `0.45` when only one teased context match is present
  - `0.30` when neither teased context match is present
  - `0.15` when the weapon provides no relevant utility/synergy for the current context
  - `0.0` when an elemental weapon misses the teased weakness entirely
- DPS off-hand effect weighting is intentionally higher than DPS main-hand effect weighting so supportive/offensive utility is rewarded.
- Non-DPS re-optimization uses weighted unique coverage thresholds and broader dedupe to avoid weak niche synergy displacing better all-around loadouts.

### Costume Slot Rules and Weighted Outfit Synergy
- Each character can contribute one main outfit and up to two sub outfits.
- Main outfit keeps full command, context, reliability, and weighted synergy value.
- Sub outfits contribute half of `(Base + Passive)` only.
- Sub outfits do not contribute command ability value.
- Outfit `SynergyMatchCount` is retained for debug visibility, but `SynergyPoints` now come from weighted match score rather than raw match count.

### Utility and Side Systems
- Summons, enemy abilities, memoria, and materia contribute additive utility bonuses.
- Utility list is capped to top contributors in final score.

### Customization Rule (Important)
- Weapon customizations unlock at `Lv80+`.
- Analyzer customization badges/details should not be suppressed solely because a weapon is `OB0/5★`.

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
- Team debug output now shows weighted synergy notes explaining which selected weapons contributed category value and how diminishing returns were applied.
- Outfit debug output distinguishes raw match count from weighted synergy points.
- If rankings look off, validate:
  - input column names
  - catalog enrichment status (`DataDiagnostics`)
  - guild rules file validity

## Player Gear Modal (Submitted Gear Viewer)
- Trigger: clicking a player's in-game name in the ranked results table.
- Data source: `PlayerGearByName` built in `PowerLevelAnalyzer.cshtml.cs` from raw submission columns (`ItemResponsesByColumnName`) for full ownership coverage (not only optimizer-selected items).
- Grouping: weapons/costumes by character, utility collections (summons/enemy abilities/memoria), materia counts, and missing catalog hints.

### Stability notes (important)
- Render modal markup outside table row/cell structures. Avoid nesting modal containers inside `tbody` row hosts.
- On page load, move `.js-gear-modal` nodes under `document.body` to avoid ancestor layout/stacking side effects.
- Open modals via explicit JS (`bootstrap.Modal.getOrCreateInstance(...).show()`) rather than mixed toggle paths.
- Use guarded hide handling so only explicit close actions dismiss the gear modal.
- While modal is open, disable `.card:hover` transform/transition effects on this page to prevent pointer-move-induced flicker.

## Common Maintenance Tasks
- Update `data/teamTemplates.json` for evolving composition strategies.
- Update `data/guildRules.json` when roster constraints change.
- Adjust synergy mappings in `SynergyDetection` and `TeamOptimizer` for new game effects.
