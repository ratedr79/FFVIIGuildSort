# Item 3 — Scoring rebuild on the real damage model (design for review)

Goal: replace the team builder's **weighted-sum heuristic** with an **estimated-team-damage** score grounded in the real multiplicative formula (the one already in `DamageCalcService` and `Shira_Damage_Calc.xlsx`) and real percentages. This makes cross-family multiplication, breakpoints, %-magnitudes, dedup, and phase-gating *emergent* instead of hand-tuned.

This is a review draft. Nothing here is committed; annotate freely. Decisions needing your sign-off are marked **[DECISION]**.

---

## 1. The scoring core — `EstimateTeamDamage(team, request)`

For a candidate team (3 characters, each with main/off/ultimate/outfit/3 subs/2 sub-costumes/materia), produce one number: estimated team damage.

**Step A — build the team buff/debuff state.** Collect every detected effect across all characters' equipped slots + the assumed-materia baseline, grouped by **family** (= one `(1+X)` term in the formula). Within a family, sum the real %s (from the tier→% tables) up to the cap; across families they stay separate factors.
- Ally buffs (PATK/MATK Up, elemental damage up, weapon boost, damage bonus, amplification, exploit weakness): a team-wide (All-Allies) source applies to every attacker; a self source applies only to its owner.
- Enemy debuffs (PDEF/MDEF Down, elemental resist down, damage-received-up, enfeeble): apply to the target → benefit every attacker.
- Apply **amplifiers** (Enliven / Applied-Stat-Tier-Increase) AFTER the base state: +2 tiers up the same table on the families present; **0 if the family is empty**.
- Apply **uptime** per family (see §3) to scale its %.

**Step B — estimate each attacker's damage.** For each damage-dealing character:
`dmg ≈ weapon% × baseConst × ∏ over families ( 1 + teamState[family]% that applies to this attacker ) × (1 − enemyResist) / enemyDEF(after debuffs)`
using on-element / on-axis terms only, mirroring `DamageCalcService`'s physical/magical branches.

**Step C — team score = Σ attackers' estimated damage.** Supports deal ~0 of their own but raised the attackers' factors in Step A, so their value is *emergent* (the increase they cause to the carry's product). This is the cross-character dependency, handled for free.

**[DECISION] Carry weighting:** sum all attackers' damage equally, or weight the primary carry higher (since a real fight funnels casts to the carry)? Leaning: weight by an assumed cast share (carry ~60%, secondary ~30%, support ~10%), configurable.

---

## 2. Effect → formula-term mapping (additive within family / multiplicative across)

| Family (one `(1+X)` term) | Effects that ADD into it | Source of % |
|---|---|---|
| attack_buff | PATK Up / MATK Up (on-axis) | active tier→% (10/20/30/40/50) |
| damage_up | Elemental Damage Up | active tier→% (10/25/40/60/80) |
| ability_potency (base block) | Boost Ability Pot, Phys/Mag Ability Pot, Element Pot, all-allies variants | passive breakpoint tables |
| defense_debuff (enemy DEF) | PDEF/MDEF Down | active tier→% (15/25/35/45/55) → DEF denominator |
| elemental_resistance_debuff | Elem. Resist Down | active tier→% (15/30/50/75/100) |
| exploit_weakness | Exploit Weakness | explicit % (30/40/60…) |
| damage_bonus | Phys/Mag/Elem Damage Bonus | explicit % |
| weapon_boost | Phys/Mag/Elem Weapon Boost | explicit % |
| ability_amplification | Amp. Phys/Mag/Elem Abilities | explicit % × count-uptime |
| damage_received_up | single/multi-tgt Phys/Mag/Elem Dmg Rcvd Up | explicit % (single-tgt N/A to AOE attackers) |
| torpor | Torpor | explicit % |
| amplifier (modifier, not a term) | Enliven, Applied Stat Buff/Debuff Tier Increase | +2 tiers on attack_buff / debuffs |

---

## 3. The four new mechanics

**3a. Amplifiers as dependency multipliers.** After Step A's base state, for each amplifier present, move the matching family up the tier table by its tier count (≈+2), capped at the table max; contributes nothing if that family is empty. (Replaces the current flat amplifier base + crude synergy bonuses.)

**3b. Uptime / maintenance.** Each family gets an uptime fraction in [0,1] that scales its %:
- A buff/debuff carried on a character's **always-cast main attacking weapon** ≈ full uptime (auto-maintained every turn).
- Otherwise uptime ≈ min(1, (baseDuration + extensions × castsPerWindow) / windowLength), improved when **multiple sources extend the same family**, and bounded by whether a **maintainer** has the spare casts/ATB.
- **[DECISION] Fidelity:** start simple — full uptime for main-weapon-carried families, a fixed high fraction (~0.85) for families a dedicated maintainer covers, lower for one-off/non-reapplied (Lucia-style start-only) effects. Refine later. Agree?

**3c. Charge-time usability.** Discount effects gated behind a long initial charge (Conformer 20s) vs turn-one usable (Rising Sun 0s). Partially modeled today; make it first-class as an uptime/availability factor.

**3d. Cross-character dependency.** Emergent from Step A (everyone's buffs merge into shared family terms), so e.g. Ragnarok's start-at-PATK-High + Aerith's PATK extension fill+maintain the attack_buff term without special cases.

---

## 4. Assembly / search (keep structure, swap the score)

The combinatorial search (team combos × variants × subs) stays — it's the runtime cost, and the formula is cheap, so swapping the scoring function shouldn't change runtime materially. The carry-first / missing-layer logic becomes *emergent* from maximizing estimated team damage:
- Highest-%, on-element, on-axis weapon naturally becomes the carry (it dominates the damage sum).
- A support is chosen when it raises the carry's product most (fills an empty/under-filled family) — the marginal-team-damage gain, which is your "compute the marginal %" rule generalized.
- Breakpoint stacking falls out (passive %s have diminishing table steps; the optimizer stops when marginal team-damage is small).
- "Don't compromise the carry for defense" is automatic — defense isn't in the offensive product, so it only wins a slot when offense is saturated or defense is requested.

**[DECISION] Scope of the swap:** replace only the per-character + team *offensive* score with the damage estimate, leaving the defensive/role/template machinery as-is? Or fuller rewrite? Leaning: targeted swap of the offensive scoring core first, to bound blast radius.

---

## 5. What's held constant (cancels in ranking)

Materia (beyond the assumed baseline), memoria, summons, guild bonuses, overspeed, base PATK/MATK: unknown and roughly constant per character across candidates, so they're held fixed and **cancel out when ranking builds**. We estimate "damage we control," not absolute damage — fine for choosing between builds.

---

## 6. Phasing & risk

This replaces the scoring core → broad test re-baselining (expected; the snapshot tests are already skipped pending exactly this). Suggested phases:
1. Build `EstimateTeamDamage` behind a flag; unit-test it against the spreadsheet's worked examples (rows 51–71) and your phys-lightning walkthrough end-state.
2. Switch the offensive team score to it; re-baseline characterization tests to the new (correct) shells.
3. Fold in uptime/charge/amplifier fidelity; re-validate against your walkthrough.

---

## Resolved decisions
- **[D1] Weight the carry.** Carry ~60% / secondary ~30% / support ~10% cast share (configurable).
- **[D2] Start simple (bucketed uptime).** Full uptime for buffs on the always-cast main attacking weapon; ~0.85 for families a dedicated maintainer covers; low for start-only/non-reapplied effects. Refine later.
- **[D3] Offensive core first.** Swap only the offensive scoring core; leave defensive/role/template machinery as-is. Focus on the ONE pattern (Lightning / Physical / Single-Enemy / Adaptive) — get the default right before broadening. (User: defaults haven't matched expectations, and UI options can't be tested until the default does.)
- **[D4] No special-casing weapons/characters — and be armory-fluid.** Keep everything weapon/character-agnostic (effect-family based). The model must degrade gracefully for REAL armories: missing costumes/weapons and weapons at different overboost levels. We've been testing with ALL items; the estimator must value whatever effects a given (possibly partial) build actually provides, with OB feeding the real %s through the snapshot. No assumption that any specific item exists.

## Phase 2 finding (team-score swap experiment)

The damage MODEL (Phase 1) and the variant→inputs extraction glue (`EstimateTeamDamage(baseVariants, request)`) are built and unit-tested. A team-score swap (blend `EstimateTeamDamage` over the legacy score, gated to offensive builds) was tried and **reverted** — it revealed the real bottleneck: **the per-character variant SELECTION is not damage-aware**, so the team-score swap can only choose among already-bad variants. Concrete evidence (phys-lightning shell): Yuffie's main = **Bird of Prey (940%)** with **Winged Chakram (1340%) in the off-hand** — the variant composition parks the *lower*-damage weapon in main, because main/off orientation is decided by R-abilities, not damage. Aerith's main = Umbrella (heal) similarly.

**Element nuance (user correction):** Bird of Prey is 940% **Water**, Winged Chakram is 1340% **Lightning**. In a Lightning battle the off-element weapon gets NONE of the weakness-exploit / resist-down / element-damage-up terms (and may be resisted), so its *effective* damage is far below 940 — Winged Chakram should win decisively. Having the main *damage* weapon in the off-hand isn't categorically wrong (actives fire from both slots) but is uncommon and wrong here because it sidelines the on-element carry weapon. **Gap to fix:** `GetVariantWeaponDamagePercent` currently uses the RAW `DamagePercent`; it must be **element/axis-gated** — an off-element / off-axis weapon's effective % is heavily discounted (no weakness/element terms apply to it), and the per-attacker estimate should apply only the buff families that match that weapon's element/axis.

So the true Phase 2 is **damage-aware variant selection**, which has two parts and is architecturally deep:
1. **Main/off orientation by damage** — ✅ **DONE (verified, zero blast radius).** `GetWeaponEffectiveDamagePercent` (element/axis-gated) + the swap block in `BuildCharacterVariants`: when either weapon is a substantial attacker (effective % > `AttackingWeaponDamageThreshold` = 500, offensive build), orient PURELY by effective damage and never let the R-ability comparison demote it. **Subtlety that took two tries:** the swap loop iterates *both* orderings of a pair, so a threshold rule alone wasn't enough — the R-ability comparison on the reverse pair (`main=Winged, off=Bird`) flipped Winged Chakram back *out* of main (Bird's passives score higher there). The fix gates the *entire* swap decision (not just an extra swap reason). Result: Yuffie main = Winged Chakram 1340% Lightning; Bird of Prey (Water) demoted to a sub. Full suite: 236 pass / 1 pre-existing fail / 7 skip — no regressions. Pure support weapons (both below threshold) keep R-ability orientation.
2. **Variant ranking by damage contribution** (still TODO): `GetVariantSelectionScore` should rank a character's builds by their contribution to estimated team damage. The seam: variants are composed and pruned to top-N *before* team context exists, so a per-character damage proxy (carry maximizes own output; support value is team-buff contribution to the carry) is needed, or the pool must keep enough diversity for a team-level pass to choose. This is the same architectural seam as team-wide dedup.

Remaining Phase 2: (2) above; Aerith-style support-main R-ability orientation (Umbrella heal currently mains for the support — lower priority, role-correct-ish); re-apply the team-score swap on the now-correct variant pool, calibrate; replace the brittle snapshot tests with property/ranking assertions.

## Validation philosophy (user) — property/ranking, NOT exact shells

There is **no single correct team** — multiple are valid and the best depends on the player's (often partial) armory. For phys-lightning the user named several plausible teams: Yuffie/Cloud/Aerith (their best — Abraxas debuff-amp + sub-DPS + full coverage), OSeph/Zack/Yuffie (slightly light on lightning resist), Yuffie/OSeph/Cid (Bravery materia to hold PATK Up after Ragnarok). So the analyzer's job is to land in the SET of good teams and avoid egregiously-wrong ones — not to reproduce one shell.

Therefore Phase 2 validation should be **property-based and ranking-based**, not exact-shell snapshots (which is exactly why the snapshot tests kept needing re-baselining — they over-pin a legitimately multi-valued output):
- **Properties of a good offensive team:** carry's main is an on-element/on-axis high-% weapon; key pyramid layers covered (PATK Up, Exploit Weakness, element resist down, element damage up, ideally damage bonus/boost + enliven/enfeeble); amplifiers (Abraxas/Enliven) paired with a base buff/debuff to amplify.
- **Anti-properties (must NOT happen):** off-element/off-axis weapon as the carry main; heal/sustain weapon as a DPS main; redundant same-layer stacking; situational (sigil/interrupt) effects counted as persistent.
- **Ranking checks:** the walkthrough team and its honorable mentions all score near the top; anomalous builds score clearly lower.
- Prefer replacing the brittle exact-shell snapshot tests with these property/ranking assertions during the Phase 2 re-baseline.

## Phase-1 unit-test targets (model, already green)
- Cross-family > same-family: two different-family +30% → ×1.69 beats one family +60% → ×1.60.
- Active tier→% resolves correctly: PATK Up High = 30%, Elem Damage Up High = 40%, PDEF Down High = 35%, Elem Resist Down High = 50%.
- Amplifier moves a present family +2 tiers (High 30% → Extreme High 50%) and is 0 with nothing to amplify.
