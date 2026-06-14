# V2 Team Search: the skeleton cap & inventory-monotonicity

> Why the guild-sort ranking raises the V2 skeleton cap, what bug it fixes, and what to check
> if "a stronger account ranked below a weaker one" ever comes up again.

## TL;DR

The V2 team search is a **two-stage funnel**:

1. **Cheap stage — skeleton "proxy":** score every candidate team, keep the top `SkeletonExpansionLimit` (Fast 30 / Full 40).
2. **Expensive stage — expansion:** run the full Backbone sub-weapon selection + accurate `EstimateTeamDamage` on only those survivors, pick the best.

The cheap stage scores each team **before sub-weapons / sub-outfits are added** (seeds carry only main + best off/ult/outfit — `BuildCharacterSeedVariants`). Sub-weapons' halved passive R-abilities push stats/amplifiers up the **breakpoint curve**, and that lift is **biased by team shape**:

- **Solo-carry teams** are already near breakpoints → subs add little → proxy ≈ accurate (ratio ~1.4×).
- **Spread/synergy teams** (3 buffing bodies, e.g. Aerith/Tifa/Zack) are far from breakpoints and the sub-passives **stack across providers and multiply into every attacker** → proxy badly under-rates them (ratio ~3.7×).

So a genuinely-best synergy team can be scored low at the cheap stage and **cut before the accurate stage ever evaluates it.** For an account with one dominant carry (a "whale" with a strong solo unit), the shortlist fills with that carry's combos and the better synergy team is evicted — so the account can rank **below** another account with equal-or-weaker gear. That violates **inventory-monotonicity**: *if account A owns everything account B owns at ≥ overboost, A must score ≥ B* (A can always field B's exact team).

## The concrete case (gb24, Wind/Physical)

- Whale **DDelaneyCA** owned rival **Jakeryan00**'s entire winning Aerith/Tifa/Zack team at ≥ overboost (verified: zero items missing).
- DDelaneyCA's copy of that team accurately scores **385,379** (above Jakeryan00's own 358,856) — but the engine handed him only **346,223** (a Vincent solo-carry team) and ranked him **#3, below the rival at #1.** A 22k shortfall that flipped the ranking.
- Root: his Aerith/Tifa/Zack seed scored ~96k on the proxy (rank ~#190–218 of ~3,645), **178 ranks below the top-40 cut**, despite a real value of 385k.

## Why raising the cap was the fix (and the cheaper options weren't)

Things that were tried/considered and **rejected**, with why:

| Option | Result |
|---|---|
| **Two-pass cut** — keep the cheap top-N shortlist, then re-rank it with a *sub-passive-credited* estimate before taking the top 40 | **FAILED.** The cheap credited estimate still under-ranked synergy teams (didn't recover the whale) **and** evicted a different account's good team (a regression). There is no cheap faithful proxy for sub-weapon stacking — a faithful pass-2 *is* a full expansion, i.e. just raising the cap. |
| **Cross-account "guard"** — each account also tries on other accounts' teams it can fully field, take the max | Viable + cheap, **provably** restores monotonicity for the *ranking*, but it's a borrowed-team patch (ranking-only — does not fix the interactive single-player analyzer) and needs a score-fixed-team entry point. Kept as a fallback, not used. |
| **Switch sub-weapon strategy to Marginal/Pro** | Masks the gap to noise but doesn't fix the root; ~87s/account. |
| **Raise the skeleton cap (chosen)** | The only **faithful** fix short of rewriting the proxy: keep more candidates through the cheap stage so the true-best team survives to the accurate stage and wins on real merit. Monotonic (raising the cap never evicts), recovers the whale, fixes the interactive analyzer too. Cost: slower — so **offline-only**. |

Two levers are needed together:

1. **`MainSeedTopN` (main-weapon breadth)** — the search only seeded each character's single best-*looking* main. The whale's best team needed a character's 2nd/3rd-ranked main (team-optimal but individually-unflashy). Seeding the top-N distinct mains gets the team **enumerated** at all. Cheap (~1% cost), zero regressions. **Engine default stays 1; the offline guild ranking opts into 2** (promoting 2 to the global default is deferred pending the spread-vs-Vincent model audit — making N=2 the default shifts the interactive analyzer's alternates list and the 422k repro signature, see below).
2. **`SkeletonExpansionLimit` cap** — even enumerated, the synergy team's low proxy score sits far below the top-40 cut. Raising the cap keeps it through to accurate scoring. Expensive (cap 250 ≈ ~5× the per-account cost) → **opt-in for the offline guild ranking only.**

Measured: min recovering cap **224**; shipped **250** (margin). Offline run ≈ ~25–30 min for ~70 accounts. Zero regressions across gb24.

## Where it lives (code)

- **Engine** `Services/PlayerPowerAnalyzerV2Service.cs`:
  - `AdaptiveSearchProfile.MainSeedTopN` — **default 1** (byte-identical engine behavior); the offline adapter opts into **2**. Promoting it to the global default is deferred (see the spread-vs-Vincent audit note).
  - `AdaptiveSearchProfile.SkeletonExpansionLimitOverride` (nullable) + `EffectiveSkeletonExpansionLimit` (`override ?? mode default`) — consumed at the skeleton-cut `.Take(...)` sites.
  - Request knobs `PlayerPowerAnalyzerV2Request.MainSeedTopNOverride` / `SkeletonExpansionLimitOverride` (both nullable, null = engine default) thread into the profile.
- **Offline opt-in** `Services/PowerLevelAnalyzerV2Adapter.cs` `BuildRequest`: sets `SkeletonExpansionLimitOverride = 250` (and mirrors `MainSeedTopNOverride = 2`). **Only the guild-ranking adapter raises the cap;** the interactive `PlayerPowerAnalyzerV2` page uses the default cap and stays fast.

## Guards / what protects this

- **`ReproSignatureRegressionTests`** — pins the Yuffie/Cloud/Aerith **422,486** headline byte-identical. The widening (N=2) and the cap are verified not to move it. `[Trait("Category","Slow")]`.
- **`PowerLevelAnalyzerV2AdapterTests.Analyze_Gb24_WindPhysical_GuildOptIn_RanksWhaleAboveRival_NoRegressions`** — the monotonicity regression guard (whale #1 above rival, no account regresses). `[Trait("Category","Slow")]`.
- **`PowerLevelAnalyzerV2MonotonicityProbeTests`** — reflective characterization of the violation/decomposition at default settings. `[Trait("Category","Slow")]`.

## If this recurs ("a stronger account ranks below a weaker one")

Check, in order:
1. **Is it really a superset?** Confirm the lower-ranked account owns the higher-ranked account's *winning team* at ≥ overboost. If not, the gap may be legitimate (name the missing item).
2. **Is the true-best team enumerated?** If the winning team uses a character's non-top main, `MainSeedTopN` must be ≥ the needed rank. (Already 2; bump if a 3rd-ranked main is needed.)
3. **Is it surviving the cut?** Dump the proxy rank of the true-best seed vs `EffectiveSkeletonExpansionLimit`. If it's enumerated but ranked below the cut, raise the cap (offline) — the proxy is under-rating it (the sub-passive bias above).
4. **Don't reach for a "cheap proxy fix"** without measuring — the two-pass attempt proved the cheap credited estimate isn't faithful. The faithful options are: raise the cap, the cross-account guard, or a from-scratch sub-passive-aware proxy.

## The real root (if anyone wants to remove the cap cost someday)

The disease is that `ScoreTeamSkeleton` (the cheap proxy) equals `EstimateTeamDamage` on a **sub-less** seed, so it is a *biased* lower bound. A proper fix would make the proxy itself credit sub-weapon passives cheaply-but-faithfully (so synergy teams rank correctly without a wide cap). That's an open, higher-touch project; the cap is the pragmatic, faithful workaround that's confined to the offline path.
