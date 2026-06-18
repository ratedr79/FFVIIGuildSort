# PlayerPowerAnalyzerV2 Max-Damage Team Building Handoff

This document is a handoff/reference for the current max-damage-oriented team-building logic in `PlayerPowerAnalyzerV2Service`.

It is intended to let another agent or developer:
- understand how the current analyzer builds and scores teams
- understand the special rules that materially affect results
- see which prior decisions/memories shaped the current implementation
- see where the feature stands now
- continue the work without re-discovering the same context

## Scope

This document covers the max-damage-oriented behavior inside:
- `Services/PlayerPowerAnalyzerV2Service.cs`
- `Services/MaxDamageReferenceCatalog.cs`
- `data/maxDamageReferenceTeams.json`
- `Tests/PlayerPowerAnalyzerV2ServiceTests.cs`
- `Tests/PlayerPowerAnalyzerV2BenchmarkTests.cs`

This is not a general page/UI doc. It is specifically about how `PlayerPowerAnalyzerV2` currently chooses offensive teams. For how the page *runs* the analysis (Full/Pro can exceed Cloudflare's 100s timeout, so it executes as an async background job with start/poll/redirect), see [Application Overview → Async Background Analysis Jobs](../architecture/application-overview.md#async-background-analysis-jobs).

## High-level intent

The current direction is:
- default offensive recommendations should form strong, coherent attack teams
- the analyzer should reward offensive shells, not just isolated weapon scores
- healer/support slots should be evaluated partly by how well they enable DPS slots
- modern stronger shells should be allowed to replace older historical shells when they clearly win
- regressions should validate strength/coherence, not force an outdated exact roster snapshot

## Core files and responsibilities

### `Services/PlayerPowerAnalyzerV2Service.cs`
Primary implementation.

Important responsibilities:
- build seed variants per character
- apply adaptive shortlisting
- build skeletons around anchor candidates
- expand skeletons into full candidate teams
- finalize the best team with sub-weapons and score breakdowns
- infer offensive role profiles
- score team-shell coherence
- apply battle-fit and passive-fit multipliers
- compute marginal sub-weapon gains with teamwide-passive awareness

Important functions to know:
- `BuildTeamCandidatesFromSkeletons(...)`
- `BuildTeamSkeletons(...)`
- `ScoreTeamSkeleton(...)`
- `BuildTeamCandidatesForCharacters(...)`
- `FinalizeTeamCandidate(...)`
- `InferOffensiveRoleProfile(...)`
- `ScoreOffensiveShell(...)`
- `ScoreReferencePatternSynergyBonus(...)`
- `GetWeaponBattleFitMultiplier(...)`
- `GetSubWeaponMarginalGainWithAnchorContext(...)`
- `ScoreTeamWidePassiveSkillForRecipient(...)`
- `BuildCharacterKeyRAbilities(...)`

### `Services/MaxDamageReferenceCatalog.cs`
Summarizes the reference-team JSON into archetype/team summaries and extracts useful signals.

Important responsibilities:
- load `data/maxDamageReferenceTeams.json`
- normalize character/item aliases
- honor `RoleHint` when present
- infer archetype-level notes such as:
  - support/healer debuff seed setup
  - triple-element DPS materia usage
  - stat-stick materia usage
  - debuff-tier-increase sources
- provide reference-pattern matching signals

### `data/maxDamageReferenceTeams.json`
Reference library used for tuning/profile extraction.

Important use cases:
- derive archetype summaries
- bias toward patterns seen in reference teams
- provide tuning ratios such as support debuff setup, debuff amplifiers, and triple-element DPS tendencies

### `Tests/PlayerPowerAnalyzerV2ServiceTests.cs`
Unit/regression coverage for role inference, shell scoring, repro diagnostics, and sub-weapon behavior.

### `Tests/PlayerPowerAnalyzerV2BenchmarkTests.cs`
Broader benchmark coverage to ensure analyzer behavior remains explainable and performance-sensitive.

## Current team-building pipeline

The current architecture is skeleton-first and anchor-first.

### 1. Build per-character seed variants
The analyzer first builds a limited set of strong seed variants for each character, centered around promising main-weapon packages.

Key idea:
- this is not a pure exhaustive full-gear Cartesian product at the beginning
- the system starts from meaningful offensive/support shells and expands from there

### 2. Adaptive character shortlist
Adaptive search can trim the candidate character pool while trying to preserve:
- requested-role anchors
- requested-effect anchors
- strong seed candidates

Adaptive and exhaustive modes now share the same skeleton-first architecture. They differ mostly in breadth controls via `AdaptiveSearchProfile`, especially `SkeletonExpansionLimit` and related shortlist breadth.

### 3. Build team skeletons
`BuildTeamSkeletons(...)`:
- ranks seed pool by `GetVariantSelectionScore(...)`
- chooses anchor candidates via `ScoreAnchorCandidate(...)`
- ranks supporting candidates around each anchor via `ScoreSupportSeedForAnchor(...)`
- forms 3-character seed trios
- enforces `mutuallyExclusiveCharacterGroups` via `IsCharacterCombinationAllowed(...)` (e.g. Sephiroth + Sephiroth (Original) can't share a team)
- scores each trio with `ScoreTeamSkeleton(...)`
- keeps top skeletons by score and distinct equipment key

**Required Characters** (`request.RequiredCharacters`, 0–3): when set, every skeleton must include all of them. Enforced here, *before* the expansion-limit cut, so a valid required combo is never pruned away:
- combo membership filter (right after the mutual-exclusion check) drops any trio missing a required character;
- required teammates are **force-included** into each anchor's support pool, so a required character that ranks low for a given anchor isn't dropped by the support `.Take` (a correctness trap, not just polish);
- when all 3 slots are pinned, anchors outside the required set are skipped — the search-narrowing speedup on large armories.
- Up-front validation in `Analyze(...)` returns a `FailureReason` (no result) for >3 required, a required character with no owned main-hand weapon, or a mutually-exclusive required pair (reuses `IsCharacterCombinationAllowed`). Default empty → byte-identical to prior behavior.

### 4. Expand skeletons into full per-character variants
`BuildTeamCandidatesFromSkeletons(...)`:
- takes each selected skeleton
- rebuilds full variant sets for the exact skeleton characters
- expansion is seeded from the seed main weapon for each chosen character
- then calls `BuildTeamCandidatesForCharacters(...)`

Important note:
- the system does not currently hard-lock a seed trio to its exact original role shape beyond the seeded character set and seeded main-weapon expansion path
- this means the final best expanded package may differ materially from the original historical shell if the expanded scoring model finds a stronger package

### 5. Evaluate full base-variant team candidates
`BuildTeamCandidatesForCharacters(...)`:
- iterates the full variant combinations for the 3 selected characters
- filters conflicts
- checks hard required effect keys
- uses an optimistic ceiling estimate to prune combinations that cannot beat the current best team
- finalizes only competitive combinations via `FinalizeTeamCandidate(...)`

### 6. Finalize best team
`FinalizeTeamCandidate(...)`:
- converts base variants to output builds
- assigns sub-weapons greedily with marginal-gain logic
- recomputes teamwide passives as sub-weapons are added
- builds final provided-effect labels
- emits score breakdowns/debug notes/key R abilities

## Major scoring layers

The current scorer is not one number. It is layered.

### Variant-level selection
Each character build candidate is influenced by:
- raw damage/stat contribution
- active offensive setup provided by main/off-hand/ultimate/outfit
- passive points
- battle-fit multipliers
- role orientation bonuses
- offensive role inference bonus/tax
- low-actual-use penalties
- contextual team bonuses

### Skeleton-level team score
`ScoreTeamSkeleton(...)` currently combines:
- sum of variant selection scores
- `ScoreEffectPackage(...)`
- `ScoreAnchorSupportSynergy(...)`
- `GetVariantContextualTeamBonus(...)`
- `ScoreOffensiveShell(...) * 0.45`
- `ScoreTeamEffects(...) * 0.12`
- `PreferredCoverageBonus(...)`
- `ScorePyramidCoverage(...)`
- hard-required effect matches
- soft-preferred effect matches
- `ScoreReferencePatternSynergyBonus(...) * 0.4`
- template penalty if the role shell is not in enabled templates

### Pre-sub-weapon team ceiling
`ComputeTeamScoreWithoutSubWeapons(...)` is used for optimistic pruning and currently includes:
- character non-passive score
- scored passives, including teamwide passives
- effect package
- anchor support synergy
- team effects
- redundant off-hand penalty
- variant alignment penalty
- contextual variant bonus
- offensive shell score weighted at `0.65`
- preferred/hard requirement bonuses
- reference-pattern synergy bonus
- template penalty

### Final team score
During finalization, the fully assembled team score includes:
- finalized main/off-hand/ultimate/outfit contributions
- finalized sub-weapon contributions
- team effects and effect-package scoring
- offensive shell scoring weighted more strongly than in the skeleton stage
- reference-pattern synergy
- final debug/explainability notes

## Offensive role inference

The current offensive-role layer exists to distinguish:
- true carries
- offensive enablers
- sustain-heavy shells
- defensive supports

### Role model
`InferOffensiveRoleProfile(...)` returns:
- `Carry`
- `OffensiveEnabler`
- `SustainSupport`
- `DefensiveSupport`

### Signals used
The inference uses:
- `directPressure`
- `enablerPressure`
- `sustainPressure`
- `defensivePressure`
- `activeSetupCount`

Derived from:
- weapon damage and stats
- active detected effects
- effect target scope
- whether the variant actually presents meaningful requested-element pressure

### Direct pressure
`ScoreVariantDirectPressure(...)` uses:
- main weapon slot pressure
- off-hand slot pressure
- ultimate slot pressure
- PATK or MATK stat contribution depending on requested damage type
- slight DPS boost for variants already tagged as `CharacterRole.DPS`

### Slot pressure notes
`ScoreVariantOffensiveSlotPressure(...)` downweights slots when they miss context:
- wrong damage type: `0.55` multiplier
- wrong element: `0.7` multiplier
- neutral/non-element slots are not automatically disqualified here

This is why additional battle-fit and shell-level penalties matter.

### Enabler pressure
`ScoreOffensiveEnablerEffect(...)` heavily values setup families such as:
- `elemental_resistance_down`
- `elemental_damage_received_up`
- `elemental_damage_up`
- `elemental_damage_bonus`
- `elemental_weapon_boost`
- `phys_damage_received_up`
- `mag_damage_received_up`
- `phys_damage_bonus`
- `mag_damage_bonus`
- `patk_up`
- `matk_up`
- `pdef_down`
- `mdef_down`
- `stat_buff_tier_increase`
- `stat_debuff_tier_increase`
- `atb_gain`
- `atb_conservation`
- `exploit_weakness`
- `enfeeble`
- `enliven`
- `torpor`

Scope matters:
- `All Allies` and `All Enemies` are rewarded more than narrow scopes
- `Self` is heavily discounted for setup effects

Off-target elemental resistance down is also penalized when a weakness is requested.

### Low-actual-use tax
The inference produces `LowActualUsePenalty` for variants that look theoretically useful but are not strong enough in practice.

Examples:
- weak enablers with low direct pressure and insufficient setup value
- sustain-heavy shells in offensive contexts
- defensive-only shells in offensive contexts
- multi-setup actives with too little real damage pressure

This tax feeds both selection and shell scoring.

## Offensive shell scoring

`ScoreOffensiveShell(...)` is the central team-coherence heuristic for max-damage behavior.

### What it rewards
It rewards teams that look like real offensive shells, especially:
- at least one carry
- dual-carry structures
- at least one carry plus at least one enabler
- 2 DPS teams
- DPS/DPS/Healer structures when the healer provides offensive value
- requested-element DPS density
- requested-element carry density
- healer slots that provide offensive multipliers or setup

### What it penalizes
It penalizes shells that drift away from actual burst structure, including:
- no carry at all
- healer template with no healer
- support-only enabler shells without healer support
- double-enabler low-DPS shells
- carry plus sustain-heavy shells with weak offensive setup
- 2 DPS shells where only one DPS matches the requested element
- carry packages that do not actually align to the requested element
- overly sustain-heavy compositions
- dual-carry no-healer/no-sustain glass shells when a healer template is expected

### Important requested-element logic
When `EnemyWeakness != Element.None`, shell scoring explicitly tracks:
- requested-element DPS count
- requested-element carry count

This means the shell layer is now intentionally trying to prefer:
- not just “DPS characters”
- but DPS characters that are actually on the requested offensive axis

## Reference-pattern synergy

`ScoreReferencePatternSynergyBonus(...)` adds a separate reference-informed bonus layer.

Current reference-driven signals include:
- support/healer debuff setup
- stat debuff tier increase paired with relevant debuffs
- triple-element DPS tendencies approximated by requested-element DPS coverage

Important detail:
- when direct reference behavior is unavailable, the scorer may assume standard synth materia can approximate some patterns
- this is currently a scoring assumption, not yet a full explicit per-character materia recommendation system

## Weapon battle-fit rules

`GetWeaponBattleFitMultiplier(...)` is one of the most important correction layers.

### DPS context penalties
For DPS weapons with damage or active effects enabled:
- wrong requested damage type:
  - `0.66` multiplier normally
  - `0.82` if the slot is an off-hand support bridge with relevant utility
- wrong requested element on a non-neutral weapon:
  - `0.78`
- neutral/non-element main weapon under a requested weakness:
  - `0.62`

This was specifically tightened so a neutral main-hand DPS package is not treated as equally valid in elemental weakness fights.

### Non-DPS fit logic
For non-DPS main/off-hand weapons, the multiplier also considers:
- offensive-effect fit
- off-target elemental resist-down practicality
- broad-defense practicality

### Why this matters
This layer is separate from shell scoring.
- shell scoring decides whether the team shape is coherent
- battle-fit decides whether a specific slot is context-correct

## Off-hand and sub-weapon rules

These distinctions are essential.

### Off-hand weapons
Off-hand weapons **can use their abilities** and are treated as active contributors.

This is a firm project assumption.

Therefore off-hand evaluation includes:
- active effects
- damage contribution
- setup contribution
- bridge utility

### Sub-weapons
Sub-weapons do **not** use abilities.
They contribute through:
- passives
- non-passive stat-like score components already embedded in their slot evaluation
- marginal gain against the current team state

## All Allies / teamwide passive stacking

This is one of the most important special cases in the current implementation.

### Teamwide passive detection
A passive is considered teamwide if its name contains `All Allies`.

Function:
- `IsTeamWidePassive(...)`

### Teamwide scoring model
`ScoreTeamWidePassiveSkillForRecipient(...)`:
- converts breakpointed passive points into effective bonus value when possible
- weights that value by recipient role

This means `Boost PATK (All Allies)` is not treated as a flat generic bonus.
It is valued more when it benefits roles that matter for the requested context.

### Recipient weighting
`GetTeamWidePassiveRecipientWeight(...)` gives different value depending on:
- passive family
- requested damage type
- recipient role

Example:
- in physical teams, `Boost PATK (All Allies)` is worth far more on DPS recipients than on a healer recipient
- the same passive is not equally valuable in magical teams

### Provider-tagged key R abilities
`BuildCharacterKeyRAbilities(...)` emits teamwide passives with provider labels.

Example format:
- `Boost PATK (All Allies) [Yuffie] +23 pts`

This is intentional and useful for debugging.
It lets you see not just that a passive exists, but which character is providing it.

### Important fix already made
A prior bug caused teamwide passive marginal gain to use the current character provider points instead of the aggregate teamwide points.
That was fixed.

Current behavior:
- saturated existing teamwide coverage lowers marginal gain for additional copies
- the analyzer no longer overvalues redundant `All Allies` passives as if they were fresh full-value additions

This especially matters for:
- `Boost PATK (All Allies)`
- `Boost MATK (All Allies)`
- teamwide defense passives
- arcanum-style teamwide passives

### Sub-weapon teamwide marginal logic
`GetSubWeaponMarginalGainWithAnchorContext(...)` and related helpers now:
- read `currentTeamWidePassivePoints`
- compute incremental gain from the current team saturation state
- apply special multipliers for offense-favoring default sub-weapon assignment
- sometimes reward distributed providers instead of over-stacking on the same character

### Practical implication
If a team already has strong `All Allies` PATK coverage, another PATK-allies passive is still useful, but much less valuable than it looked before the fix.

## R ability breakpoint handling

The analyzer does not treat passive points linearly for key offensive families.

Project-level breakpoint tables were provided and are now important to the design direction.
Relevant supported families include:
- `Boost PATK/MATK`
- `Boost Ability Pot.`
- `Boost Ability Pot. (All Allies)` — resolved with its **own** breakpoint table (caps at Lv.3 = +15%), not the self `Boost Ability Pot.` table it previously fell through to
- `Boost [Element] Pot.`
- `Boost Phys./Mag. Ability Pot.`
- `[Element] Ability (All Allies)`
- `Boost PATK/MATK (All Allies)`
- `Boost ATK (All Allies)`
- `Boost ATK`
- `Boost PDEF/MDEF (All Allies)`

Practical implication:
- passives near a breakpoint can be much more important than a naive linear point model would suggest
- marginal-gain logic and teamwide saturation logic should always be read with breakpoints in mind

## Sub-weapon assignment overview

Final sub-weapon assignment is greedy but context-aware.

### How it works
`FinalizeTeamCandidate(...)`:
- starts from finalized base variants
- maintains per-character current passive points
- maintains current aggregate teamwide passive points
- repeatedly assigns the best remaining sub-weapon by marginal gain

### What marginal gain includes
`GetSubWeaponMarginalGainWithAnchorContext(...)` includes:
- non-passive score contribution
- battle-fit multiplier for DPS only
- incremental passive score
- teamwide passive incremental value using current saturation
- anchor-aware considerations
- requested-element awareness

### Why anchor context matters
Sub-weapons are not evaluated in a vacuum.
They are partly evaluated by how well they strengthen:
- the equipping role
- the overall shell
- the current anchor/carry package

## Reference catalog behavior

`MaxDamageReferenceCatalog` extracts useful shape signals from `maxDamageReferenceTeams.json`.

### What it currently captures
For each team/archetype, it tries to understand:
- weakness
- preferred damage type
- role shell
- character overlap
- support/healer debuff seed setup
- triple-element DPS loadouts
- stat-stick materia usage
- stat debuff tier increase sources

### Important current behavior
`RoleHint` is honored when present.
If not present, the catalog falls back to `CharacterRoleRegistry.GetRoleOrDefault(...)`.

### Why the reference catalog matters
The current implementation does not hard-force reference teams.
Instead it uses the reference dataset to bias the scorer toward patterns that appear repeatedly in real high-performing teams.

## Current tests and what they assert

### Unit/regression tests in `PlayerPowerAnalyzerV2ServiceTests.cs`
Important current coverage includes:
- `InferOffensiveRoleProfile_RequestedElementDpsWithMeaningfulSetup_RemainsCarry`
  - protects requested-element DPS carries from being misclassified as pure enablers
- `ScoreOffensiveShell_DualRequestedElementDpsHealer_BeatsSingleCarryDoubleEnablerShell`
  - protects coherent DPS/DPS/Healer burst shells
- `Analyze_ExportedReproJson_2026_06_02_23_32_33_2_SelectedTeamDiagnostics`
  - real repro regression
  - now validates that the chosen shell is genuinely strong/coherent rather than forcing an older exact Sephiroth roster snapshot
- `FinalizeTeamCandidate_LightningPhysical_ExactReportedCurrentShell_ReportsNamedPoolMarginalGains`
  - protects explainability of named-pool sub-weapon marginal gains for a known shell

### Benchmark coverage in `PlayerPowerAnalyzerV2BenchmarkTests.cs`
Important current benchmark themes:
- explainability and score-breakdown consistency
- adaptive vs exhaustive performance comparison
- both search modes exposing skeleton/anchor debug notes
- several “support package beats raw-stat alternative” scenarios across elements/characters

## Project memories and prior decisions that matter

These are the important remembered decisions/context items that shaped the current implementation or should shape future work.

### 1. Strength over historical exactness
Current preference:
- regressions should validate whether the selected team is actually stronger and still forms a coherent offensive shell
- they should not force an exact historical roster snapshot

Reason:
- modern weapons can legitimately create stronger shells than older reference-era teams
- example discussed: a Cloud-based modern shell beating an older OSeph shell because of weapons like `Noble Parasol` and `Erdrick's Sword`

### 2. Skeleton-first anchor-first architecture is intentional
The analyzer was redesigned so both adaptive and exhaustive modes share:
- seed variant generation
- adaptive shortlisting
- skeleton building around anchors
- skeleton expansion into final candidates

This is the current intended architecture, not a temporary experiment.

### 3. Qualitative potency / threshold-sensitive setup parsing matters
Active-effect scoring now uses qualitative tier parsing from ability text for threshold-sensitive setup effects.
This matters for:
- attack buffs
- defense debuffs
- elemental resistance/damage setup

It also includes a ramp factor for effects that only become strong after repeated casts.

### 4. Off-hand abilities are real active utility
Project rule:
- off-hand weapons can use their abilities and must be considered for active utility just like main-hand weapons
- only sub-weapons lack active ability use

### 5. Non-DPS slots should be team-facing in offensive shells
Project rule:
- healer/support/tank scoring in offensive teams should favor ally buffs, enemy debuffs, elemental setup, defensive ally buffs, and useful offensive utility
- self-only offensive R abilities on low-damage support builds should not dominate scoring

### 6. Teamwide passive saturation fix is important and should not be undone
The marginal-gain fix for `All Allies` passives changed late-round sub-weapon ordering in useful ways.
Current expected behavior relies on aggregate teamwide saturation, not naive per-provider scoring.

### 7. Breakpointed R-ability tables are important design inputs
The scorer should continue to respect real breakpoint tables rather than assuming linear value.

### 8. Magical-team design note still matters
A prior water/magical planning example emphasized:
- identify the true carry first
- fill missing multiplier families around that carry
- prefer differentiated amp families over selfish duplicates
- treat costumes more as utility vehicles than raw weapon-scaling damage vehicles
- allow boss-specific pivots like Torpor or extra defense later

This is not fully encoded as a separate subsystem, but it is a useful design lens for future tuning.

### 9. Synth-materia surfacing is still desired
A remembered future direction is to surface assumed synth-materia coverage as explicit per-character materia recommendations, especially when the scorer is assuming missing elemental debuff coverage can be synthesized.

## Where team building stands now

Current state as of this handoff:

### What is working well
- anchor-first skeleton building is in place
- adaptive and exhaustive share the same architecture
- offensive role inference exists and is wired into selection/shell scoring
- offensive shell scoring now recognizes carry/enabler/sustain distinctions
- requested-element DPS density matters at the shell level
- battle-fit penalties exist for wrong-axis and neutral main-hand DPS packages
- reference-team patterns inform scoring without hard-forcing exact compositions
- teamwide `All Allies` passives use aggregate saturation in marginal-gain logic
- real repro coverage exists for a lightning/physical case
- explainability remains strong through debug notes, key R provider labels, and score breakdowns

### Important current behavioral stance
The analyzer is currently allowed to select a new modern shell instead of an older reference-like shell if:
- the shell is coherent
- the requested offensive axis is respected
- the score model says it wins by a meaningful margin

This is now intentional.

### Current lightning physical repro stance
The historical repro involving `Aerith + Sephiroth + Yuffie` was re-evaluated.
The current regression no longer requires exact Sephiroth inclusion.
Instead it requires:
- a coherent lightning/physical offensive shell
- strong setup coverage
- multiple requested-axis DPS members
- a meaningful score margin if a non-Sephiroth shell replaces the older shell

## Known outstanding work / likely next improvements

These are the most relevant unfinished items.

### 1. Explicit synth-materia recommendations are still unimplemented
Current state:
- the scorer can assume standard synth materia coverage for some missing patterns
- but this is mostly implicit in scoring/debug reasoning

Wanted future behavior:
- surface assumed synth coverage as explicit per-character materia recommendations
- especially for missing `elemental_resistance_down` in weakness-focused teams

### 2. ATB cost is not yet a first-class tuning signal everywhere it could be
There is already some timing/charge handling, but a remembered future direction is:
- make ATB cost matter more directly in heuristic decisions
- especially when choosing among similar setup providers, such as faster PATK-up options

### 3. Expansion-phase shell mutation is not explicitly locked down
Current state:
- skeleton expansion can still materially change the exact character package chosen by the seed stage
- this is now tolerated when the result is actually stronger

Possible future work:
- add a direct role-shape preservation bonus/tax during expansion
- or add diagnostics showing how far the final expanded shell drifted from the seed shell

Important note:
- this is not currently considered a must-fix defect, because the test direction changed from “exact historical team” to “actually stronger shell”

### 4. Cross-element anchor heuristics likely still need more tuning
The lightning/physical case received the most recent tuning attention.
Likely future work remains for:
- magical teams
- anchor-first weighting around a true main carry
- choosing differentiated multiplier families rather than duplicating selfish damage packages

### 5. More explicit shell diagnostics could still help
Useful future additions could include final result notes such as:
- inferred offensive role per character in final output
- seed-shell vs final-shell comparison
- requested-element carry counts / DPS counts in debug notes

### 6. Reference-pattern assumptions could be made more visible
The reference catalog currently influences scoring, but the final output could expose more of that reasoning, for example:
- which reference archetype matched best
- what matching signals were used
- where synth assumptions substituted for direct gear coverage

## Special notes that another agent should remember

### `All Allies` does not mean infinite full-value stacking
Allies-wide passives are now saturation-aware.
Do not assume another copy of the same family is worth full fresh value.

### Provider identity matters in debugging
When reviewing final `KeyRAbilities`, teamwide passives are provider-tagged.
This is intentional and helpful.

### Off-hand is active, sub-weapon is passive-only
Do not collapse these concepts.
Many offensive-support packages depend on off-hand actives being real contributors.

### A healer can still be offensively valuable
The current model intentionally rewards healer slots that provide:
- PATK/MATK up
- elemental damage up
- debuff tier support
- gear-use extensions
- other real offensive amplification

### Reference teams are guidance, not a lockfile
The analyzer uses real-team patterns as tuning input, not as strict mandatory outputs.

### Neutral or wrong-axis DPS mains are intentionally penalized in elemental fights
If a weakness is requested, a neutral main-hand DPS weapon should not be treated as equally valid just because it has high raw stats.

## Recommended next places to inspect when continuing work

If continuing tuning, start here:
- `PlayerPowerAnalyzerV2Service.InferOffensiveRoleProfile(...)`
- `PlayerPowerAnalyzerV2Service.ScoreOffensiveShell(...)`
- `PlayerPowerAnalyzerV2Service.ScoreReferencePatternSynergyBonus(...)`
- `PlayerPowerAnalyzerV2Service.GetWeaponBattleFitMultiplier(...)`
- `PlayerPowerAnalyzerV2Service.GetSubWeaponMarginalGainWithAnchorContext(...)`
- `PlayerPowerAnalyzerV2Service.BuildTeamSkeletons(...)`
- `PlayerPowerAnalyzerV2Service.BuildTeamCandidatesFromSkeletons(...)`

If continuing reference-driven tuning, inspect:
- `Services/MaxDamageReferenceCatalog.cs`
- `data/maxDamageReferenceTeams.json`

If validating behavior, start with:
- `InferOffensiveRoleProfile_RequestedElementDpsWithMeaningfulSetup_RemainsCarry`
- `ScoreOffensiveShell_DualRequestedElementDpsHealer_BeatsSingleCarryDoubleEnablerShell`
- `Analyze_ExportedReproJson_2026_06_02_23_32_33_2_SelectedTeamDiagnostics`
- `PlayerPowerAnalyzerV2BenchmarkTests`

## Suggested next implementation priorities

Reasonable next priorities are:
1. surface synth-materia assumptions as explicit output recommendations
2. expose more final-shell diagnostics in user/debug output
3. add more magical-team and non-lightning regression coverage
4. consider whether expansion-phase shell drift needs explicit scoring or diagnostics
5. incorporate ATB-cost sensitivity more directly into provider ranking

## Short summary

`PlayerPowerAnalyzerV2` currently builds offensive teams by:
- starting from seeded anchor-centric variants
- forming skeleton teams
- expanding them into full candidate packages
- preferring coherent carry/enabler/healer shells
- rewarding requested-axis pressure and offensive setup
- valuing teamwide passives with real breakpoint and saturation behavior
- using reference teams as pattern guidance rather than exact forced outputs

The most important current design decision is:
- **prefer the actually stronger coherent team, not the historically familiar one**.
