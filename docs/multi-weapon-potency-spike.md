# Spike: Multi-Weapon Potency Blindspot in the V2 Damage Model

**Status:** Finding B FIXED + re-baselined (2026-07-01). Finding A still pending (design agreed).

**Finding B — DONE.** Fixed at all three damage-value multiplier sites: `GetWeaponEffectiveDamagePercent`
(DamageModel.cs:617, → 0.85 neutral), `ScoreDamage` (Service.cs:7192, → 1.0 neutral), and
`ScoreVariantOffensiveSlotPressure` (Service.cs:1909, → 1.0 neutral). Repro re-baselined
**422486.07 → 572874.89** (winner Yuffie|Cloud|Aerith + gear stable). Two ITB score-repro tests
re-baselined (SPREAD 385,379 → 626,687; VINCENT 356,030 → 411,877 — B widened the spread-vs-Vincent gap,
see the deferred spread-team audit). REMAINING as follow-up: the boolean on/off-element *classification*
gates (Service.cs:1539, 2697, 2948, 5956, 7347, 7592) still match only "None"/empty — they decide
candidate-set membership, not damage scaling, so they need per-site judgment separately. One behavioral
test (`Analyze_WindPhysical_AvoidsReportedOffFitMainAndOffHandChoices...`) went red — see the A×B note
below; it is a Finding-A symptom, not a B regression.
**Trigger:** Titan EX3 team review — engine chose Sephiroth's *Wintercrest Blade* (290% off-hand)
over *Serrated Remiges* (1120% off-hand).
**Repro harness:** `FFVIIEverCrisisAnalyzer.Tests/SpikeMultiWeaponPotencyTests.cs` (Tests-only, reads
LIVE weapon-service/JSON values; the numbers below are its output, not the backup TSV).

---

## TL;DR

Two independent defects, both in the damage model's per-weapon handling:

- **Finding A — multi-weapon blindspot (large).** Each character is collapsed to ONE weapon via
  `Math.Max(main, off, ultimate)`. Both the main and off-hand abilities are real attacks that weave
  in the rotation, but the model keeps only the single highest. For Sephiroth the off-hand swap is
  worth **~830%** effective potency (Serrated 1120 vs Wintercrest 290); the model sees only **120%**
  of it.
- **Finding B — "Non-Elemental" misread as resisted (medium, likely independent).** The neutral-
  element guard matches only `"None"`/empty, but the live data string is `"Non-Elemental"`, so every
  non-elemental weapon eats the **×0.5 off-element resist penalty** instead of the intended **×0.85**
  neutral factor — a ~41% under-rate on any weakness fight.

The two interact (see below), which is why my first read of this team was wrong in both directions.

---

## Live numbers (OB10 / max level, Titan EX3 = Wind weakness / Magical)

| Slot | Weapon | pot% | **eff%** | element | notes |
|---|---|---|---|---|---|
| main | Protector's Blade | 900 | **450** | Non-Elemental | ×0.5 (Finding B; should be 765) |
| off A | Serrated Remiges | 1120 | **1120** | Wind | on-weakness ×1.0 |
| off B | Wintercrest Blade | 290 | **290** | Wind | on-weakness ×1.0 |
| ultimate | Metsumato | 2000 | **1000** | Non-Elemental | ×0.5 (Finding B; should be 1700) |

Representative attack % for Sephiroth (what feeds the team headline):

| | Serrated off-hand | Wintercrest off-hand | delta |
|---|---|---|---|
| **CURRENT** `max(main,off,ult)` | 1120 | 1000 | **120** |
| **Option-A** uptime blend | 828 | 496 | **332** |
| **True rotational swing** (both fire) | — | — | **830** |

---

## Finding A — the blindspot (code refs)

- **[DamageModel.cs:633-639]** `GetVariantWeaponDamagePercent` = `Math.Max(main, off, ultimate)`.
- **[DamageModel.cs:829-875]** team damage = `Σ_character ( repWeapon% × CarryCastShares[0.6/0.3/0.1] × multiplier )`.
- Non-max weapons still contribute their **buffs/debuffs** (separate effects layer); only their **raw
  attack potency** is discarded.
- **[DamageModel.cs:772]** comment frames the off-hand as *"the opportunity cost of casting an
  OFF-HAND ability INSTEAD of attacking"* — i.e. it assumes one attacking weapon per character. In EC
  both abilities are real attacks, so this is the bug.

**Two-sided distortion:** the model *under*-credits the sustained weapons (main+off) AND *over*-credits
the burst ultimate — Metsumato is `UseCount 3`, charge-gated, yet taken as the rep% at the full 0.6
carry share as if it swings every turn. That is why, even on today's numbers, the off-hand's value is
crushed to a 120 sliver poking above the ultimate.

**Note on the original team decision:** with live values the current `max` model actually scores the
Serrated loadout *higher* on rep% (1120 > 1000). So the engine's Wintercrest pick is **not** a
weapon-% call — it comes from the effect multiplier (passives: Wintercrest's 3rd MATK Boost vs
Serrated's dead Triangle sigil boost + a deduped Enfeeble) and/or Fast-mode off-hand pruning. That is
a *separate* open thread from the blindspot; the blindspot is that neither off-hand's real damage is
being counted.

## Finding B — "Non-Elemental" resist penalty (code refs)

- **[DamageModel.cs:617-626]** `GetWeaponEffectiveDamagePercent`: the neutral branch fires only for
  `IsNullOrWhiteSpace(Element) || Element == "None"` (→ ×0.85, comment: *"non-elemental: no weakness
  bonus, but not resisted"*). The data string is `"Non-Elemental"`, which fails that guard and falls to
  `!MatchesRequestedElement(...)` → **×0.5** (`OffElementDamageFactor`).
- **[Service.cs:5052]** the slot is built with `Element = weapon.Item.Element` verbatim (no
  normalization), so the production hot path really does see `"Non-Elemental"`.
- **[Service.cs:7896-7897]** the costume path already checks BOTH `"None"` and `"Non-Elemental"` — so
  the codebase is internally inconsistent and the damage-model hot path has the older, narrower guard.
- **Fix:** add `"Non-Elemental"` to the neutral guard (one line). Small, self-contained, and it makes
  the numbers match the code's own stated intent. Still moves the repro (any non-elemental attacker on
  a weakness fight changes), so it re-baselines too.

**Interaction with A:** fixing B alone lifts Metsumato to 1700, which would then shadow *both*
off-hands (1120 and 290) → the two loadouts tie on rep% again, so the off-hand potency stays invisible.
In other words B makes A *more* visible, not less. They should be evaluated together.

---

## Option-A model (recommended first fix for A)

Replace `max` with `repWeapon% = Σ_slot w_slot · eff(slot)`, weights summing to 1:
- **main & off**: split the non-ultimate budget inversely by `CommandAtb` (cheaper cast = more
  frequent). Equal here (both CmdATB 4) → even split.
- **ultimate**: a fixed charge/use-limited share (`UltimateUptimeShare`, prototyped at **0.20**) —
  the key calibration knob; `InitialChargeTimeSec`/`UseCount` can refine it later.

Result: Serrated 828 vs Wintercrest 496 (delta 332) — recovers far more of the true 830 swing than
max()'s 120, and de-weights the over-credited ultimate (blend < max). All inputs already live on
`PlayerPowerAnalyzerV2ItemSlot` (`CommandAtb`, `InitialChargeTimeSec`, `UseCount`) — no new plumbing.

Option B (additive throughput, `Σ casts·eff`) remains the more faithful but higher-blast-radius
alternative; hold unless Option-A under-models burst carries.

## Blast radius / risks

- **Repro signature (422486.07) changes** for BOTH fixes — deliberate corrections, so re-derive and
  re-approve the baseline; don't hold it byte-identical.
- `GetVariantWeaponDamagePercent` / `GetWeaponEffectiveDamagePercent` run inside **candidate
  generation** (carry ranking, off-hand gate, skeleton seeding), not just final scoring → needs a
  **full-search** regression sweep, not just the repro number.
- **Weight calibration (A)** is the real design risk: too much ultimate weight reproduces the bug;
  too little under-rates real burst carries. Needs a small hand-graded reference set.

## Recommended sequencing

1. **Finding B** — DONE (2026-07-01). Landed + re-baselined.
2. **Finding A** — scoped below; awaiting go-ahead.
3. (Optional) chase the **Wintercrest-vs-Serrated pick** through the full analyzer once A lands.

---

# Finding A — implementation scope

## Chokepoint & blast radius

The entire change is one function: **`GetVariantWeaponDamagePercent`** (DamageModel.cs:639), the sole
producer of a character's representative attack %. It has ONE direct caller — `EstimateTeamDamage`
(DamageModel.cs:687) — but `EstimateTeamDamage` has **~11 callers**, so the blend propagates to:

- **final team scoring** (the headline);
- **candidate generation** — off-hand gate (Service.cs:608, 658), main-weapon seed ranking (815),
  skeleton scoring (1349), sub-weapon marginal selection (4406, 5417);
- **pruning ceilings** (Service.cs:4007, 4139) — *the subtle one, see risks*;
- **ITB Est. Dmg** (InteractiveTeamBuilder.cs:763).

Inside `EstimateTeamDamage`, the rep% is used three ways: (a) **ranking** attackers to pick the carry
(`OrderByDescending(x => x.Weapon)`), (b) **always-cast** detection (`weapon > AttackingWeaponDamageThreshold`),
and (c) the **damage sum** (`ranked[i].Weapon × share × multiplier`). All three shift with the blend.

## The new rep% (Option A — uptime-weighted blend)

```
repWeapon% = Σ_slot  w_slot · GetWeaponEffectiveDamagePercent(slot, request)
```
over the character's castable weapons {main, off, ultimate}, weights summing to 1:
- **main & off** split the non-ultimate budget `(1 − wUlt)` inversely by `CommandAtb` (cheaper cast =
  more frequent); equal `CommandAtb` → even split. Both are always-cast attacks in the rotation.
- **ultimate** gets a fixed charge/use-limited share `wUlt` (prototype **0.20**) — the one calibration
  knob. Optionally refine later from `InitialChargeTimeSec` + `UseCount`, but a constant is the v1.

Prototype output (Sephiroth, Titan EX3, post-B): Serrated off-hand **1094** vs Wintercrest **762** —
recovers the real separation the `max()` model shows as 0, and de-weights the ultimate (blend < max).

## Key design decision — element gating inside the blend

The blend can mix weapons of DIFFERENT elements (e.g. non-elem main + Wind off + non-elem ult). Two ways
to apply the weakness-exploit / elemental effects:

- **Tier 1 (recommended v1):** keep the existing **per-character, any-weapon** weakness gate
  (`AttackerCanHitWeakness` already returns true if ANY of main/off/ult hits the weakness) and apply the
  element-dependent effects to the whole blended output. This is *no worse* than today — the current
  `max()` model already sources the base % from one weapon while gating on any weapon, so Tier 1 is
  actually MORE internally consistent, and it's a far smaller change. Risk: slightly over-credits
  element effects to a character whose on-weakness weapon is only a small slice of the blend.
- **Tier 2 (faithful, later):** compute per-weapon multipliers inside the blend so weakness-exploit hits
  only the on-weakness slices. Bigger refactor of `EstimateTeamDamage` (per-weapon, not per-character,
  multipliers). Do only if Tier 1 measurably over-credits split-element carries.

## Risks specific to A

- **Pruning-ceiling validity — AUDITED 2026-07-01: SAFE, no rework needed.** The concern was that a
  lower-valued blend could break the `ceiling ≥ final` invariant. It does not, and the reason is
  structural:
  - Final (`FinalizeTeamCandidate`, Service.cs:4406) damage term = `EstimateTeamDamage(creditedVariants)`;
    ceiling (`ComputeTeamScoreWithoutSubWeapons`, Service.cs:4007) damage term =
    `EstimateTeamDamage(damageVariants)`, where `damageVariants` = base + **optimistic** subs and
    `creditedVariants` = base + **actual** subs. Both add the **same** refinement terms.
  - The ceiling's ONLY optimism is the sub-passive set (`optimistic ≥ actual`). `EstimateTeamDamage` is
    monotonic in sub-passive contributions (buffs ≥ 0), so `ceiling ≥ final` — independent of how rep% is
    computed.
  - The blend lives in `GetVariantWeaponDamagePercent`, which reads ONLY main/off/ult (never subs), so
    the rep% is **identical** in ceiling and final (same base weapons) and cancels out of the inequality.
    Carry ranking (`OrderByDescending(x => x.Weapon)`) is likewise weapon-only → identical in both.
  - Single chokepoint ⇒ the blend is applied consistently to every `EstimateTeamDamage` caller (final,
    ceiling, leader), so pruning stays exact. **Conclusion: swapping max()→blend needs no ceiling change.**
  - (Pre-existing, orthogonal: the ceiling weights `offensiveShellScore` at 0.65 vs the final's 0.75 —
    not touched by the blend; whatever slack that introduces is unchanged.)
- **Carry re-ranking.** Ordering attackers by the blend (not max) can change WHO is the carry (0.6
  share), which cascades through the whole multiplier partition. Expected, but a big source of churn.
- **Another re-baseline.** Repro + the two ITB score tests move again; the parked `AvoidsReportedOffFit`
  test should flip back to GREEN (its whole premise is that a better off-hand wins on damage) — that's
  its acceptance signal.
- **Calibration.** `wUlt` and the main/off split need a small hand-graded set. Anchors on hand:
  Sephiroth/Titan (Serrated must beat Wintercrest), the parked test (Demon's Impetus must beat
  Lightning's Gloves for Tifa's off-hand), plus a few known single-carry teams that must NOT regress.

## Phased plan (each step gated)

1. **Flag + implement — DONE (2026-07-01).** `request.EnableMultiWeaponPotencyBlend` (default FALSE →
   byte-identical). `GetVariantWeaponDamagePercent` branches to `BlendVariantWeaponDamagePercent` when set:
   regular (main+off, eff>0) weapons split `(1 − UltimateRotationShare)` inversely by `CommandAtb`; ultimate
   takes `UltimateRotationShare = 0.20`. Anchor test `GetVariantWeaponDamagePercent_MultiWeaponBlend_
   IsGatedByFlag_AndCountsOffHandDamage` calls the real method (flag off → max, both off-hands identical;
   flag on → Serrated ≫ Wintercrest, blend < max). Non-Slow suite (276) + repro (572874.89) green — the
   default path is untouched.
2. **Ceiling audit — DONE (2026-07-01, above): SAFE.** No pruning changes needed.
3. **Full-search sweep** with the flag on — compare rankings vs today on the repro + a handful of real
   inventories; confirm the parked `AvoidsReportedOffFit` test flips green; calibrate `UltimateRotationShare`
   (and decide whether a 0-damage off-hand should take a rotation slice — currently excluded). NEXT.
4. **Flip default, re-baseline** repro + ITB tests, un-skip the parked test — one reviewed change.
