# Boss-Driven Analyzer V2 + Required Sigils — Design

Status: planned (spike complete; boss→sigil data already built into `EnemyCatalog`/Enemy Stats).

## Goal
Let a player either set battle context manually (today: Enemy Weakness, Preferred Damage Type, Target Scenario) **or** select an enemy and have the analyzer derive that context — including **sigil requirements** — to recommend a team. Also add a team-level **Required Sigils** capability the player can set manually (advanced filters), independent of boss selection.

## Sigil model (agreed)
We model sigil **capability**, not counts/timers. (Counts vary wildly — a phase can need <10 or >50; any attack in Attack Stance breaks 1 sigil; matching sigil materia and main-hand **sigil-boost** slots amplify how fast you break. We don't know the count per boss, so we don't simulate it.)

A character **covers** a sigil when:
- **◯ Circle / ✕ Cross / △ Triangle** → their **MAIN-HAND** weapon has a matching **sigil-boost materia slot** (`SigilInfo.Source == "Materia"`, type matches; Level I or II). Off-hand / sub-weapon slots do **not** count. Level II > I (soft tiebreaker / bonus weight).
- **◊ Diamond** → a **main- or off-hand** weapon's **ability command sigil** is Diamond (`SigilInfo.Source == "Command"`, type Diamond). Diamond has no materia slot — it's a weapon ability, usable from main or off hand.

Two strengths:
- **Required sigil** (manual advanced filter, or a boss's **break** sigil) → **hard team-level filter**: at least one character covers it; teams with no coverage are excluded.
- **Damage sigil** (boss only) → **soft score bonus**: scales with how many of the boss's damage sigils the team can cover (level-II boost adds a bit more). Situational — any team can equip a base matching materia, but boost slots amplify; "highly suggested," matters more on harder bosses. Never a filter.

If a boss has **no** sigil data, no sigil requirement/bonus is applied (handles "situational whether there's even a sigil phase").

## Data sources (all confirmed/available)
- **Enemy** (stats, resistances, immunities, description, sigils): `EnemyCatalog.GetEnemyDetails(enemyId, level)` → `EnemyDetailView` with `PhysicalDefense`/`MagicalDefense`, `ElementResistances`, `BuffDebuffImmunities`, `Description`, **`BattleSigils`** (break) and **`BattleDamageSigils`** (damage). The break/damage split + enemy join are already built and verified (Enemy Stats "Battle Sigils").
- **Weapon sigils**: `WeaponSearchItem.Sigils` (`SigilInfo`: `SigilType`, `SigilSymbol`, `Level`, `Source ∈ {Command, Materia}`) + `CommandSigil`. Already reachable on the V2 candidate via `item`.

## Phase 1 — Required Sigils in V2 (manual)
- **Request:** `RequiredSigils: List<string>` on `PlayerPowerAnalyzerV2Request` (values Circle/Triangle/Cross/Diamond). Default empty → no behavior change (analyzer repro byte-identical).
- **Engine:** a **team-candidate filter** (team-level, like `HardRequiredEffectKeys`) applied after team candidates are built — NOT a seed filter (avoids the monotonicity-sensitive seed stage). A team passes only if, for **each** required sigil, ≥1 character covers it per the matching rules above.
  - Helper `TeamCoversSigil(team, sigil)`: for C/X/T scan each character's **Main** `Sigils` for a `Materia` slot of that type; for Diamond scan each character's **Main and Off** for a `Command` Diamond.
- **UI:** four sigil checkboxes (◯△✕◊) in the Advanced filters accordion, bound to `RequiredSigils` (mirrors Boss Immunities / Required Characters). Available regardless of boss selection.
- **Failure feedback:** when nothing matches, `FailureReason` names the unmet sigil(s).
- **Tests:** `TeamCoversSigil` units (main-hand C/X/T covers; off-hand C/X/T does NOT; Diamond covers from main or off); a filter test; repro byte-identical when empty.
- **Effort:** Low–Medium.

## Phase 2 — Enemy selection (either/or)
- **Mode toggle:** **Manual** (today's Weakness / Damage Type / Scenario + Phase-1 manual sigils) **or** **By Boss** (enemy picker, reusing the Enemy Stats search + suggestions).
- On boss select, derive and **display** the inferred context (editable overrides, not a hard replace):
  - **Weakness** ← element with the most-negative resistance (e.g. Wind −100%); None if no negative resistance.
  - **Damage type** ← lower of PDEF/MDEF, **refined** by: def-down-resist asymmetry (resists *PDEF Down* but not *MDEF Down* → Magical, and vice-versa); a description-hint scan ("…magic abilities are effective"/"…physical abilities are effective"). Truly equal & no signal → Any.
  - **Scenario** ← enemy count in the battle's enemy group (1 → Single Enemy; >1 → Multiple).
  - **Required sigils** ← boss **break** sigils (hard). **Damage sigils** ← soft bonus.
- **Plumbing:** page model injects `EnemyCatalog`; a boss-selection handler returns the derived context from `EnemyDetailView`; `BuildRequest()` sources from the derived context (By Boss) or the manual inputs (Manual).
- **Scoring — damage-sigil bonus:** after a team passes filters, add a modest soft bonus proportional to the count of the boss's damage sigils the team can cover (small extra for Level-II boost). Tunable; gated to By-Boss mode. Keep it a nudge, not a dominator (coverage is situational).
- **Effort:** Medium (mostly UI + derivation + plumbing; the data and the sigil-coverage engine land in Phase 1 / Enemy Stats).

## Open / deferred
- Per-stage vs per-enemy **union** of sigils (Enemy Stats currently unions across an enemy's battles) — refine to per-stage if it proves noisy for the analyzer.
- Description-hint parsing is heuristic — start with a couple of phrases, expand only if useful.
- Sigil **count/timer** is intentionally not modeled.
- Boss break sigils as a hard filter could over-prune; mitigated by the override (By-Boss values are editable). Revisit as a soft preference if it excludes viable teams in practice.

## Build order
Phase 1 first (self-contained, immediately useful via advanced filters), then Phase 2 (enemy selection layering on top).
