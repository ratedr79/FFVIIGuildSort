# Sigil Effect Mapping Inference Notes

This note explains the current `BuffDebuffType` sigil-resistance mappings used by `WeaponSearchDataService` and why some of them are treated as inference rather than direct master-data labels.

## Why this note exists

The FF7EC master data currently exposes the relevant `BuffDebuffType` IDs, but not all of them are attached to a clean, localized skill label that directly spells out the final player-facing meaning. For the Ultima Weapon fight family, the final mappings were derived by combining:

- confirmed in-game footage
- master-data tracing through `SkillBuffDebuff.json`, `SkillEffect.json`, `SkillEffectGroup.json`, `SkillBase.json`, and related enemy-skill tables
- the structure of the sigil up/down buff groups in `BuffDebuffGroup.json`

If future FF7EC data or new fight footage contradicts any inference below, update the lookup table in `Services/WeaponSearchDataService.cs` and the regression tests in `FFVIIEverCrisisAnalyzer.Tests/WeaponSearchDataServiceTests.cs`.

## Current accepted mapping

### Directly confirmed or strongly confirmed from footage plus data
- `43` -> `Circle Sigil Resistance Up`
- `44` -> `Triangle Sigil Resistance Up`
- `45` -> `X Sigil Resistance Up`
- `46` -> `Diamond Sigil Resistance Up`
- `49` -> `Triangle Sigil Resistance Down`
- `50` -> `X Sigil Resistance Down`
- `StatusChangeType 1` -> `Regen`

### Accepted inference mapping
- `48` -> `Circle Sigil Resistance Down`
- `51` -> `Diamond Sigil Resistance Down`

### Reserved / currently unused family slots
- `47` -> no active `SkillBuffDebuff` rows found in current GL master data
- `52` -> no active `SkillBuffDebuff` rows found in current GL master data

These appear to be defined in the family groups but unused by current live skill applications. They may be reserved for future sigils, alternate event content, or simply unused schema space.

## Evidence summary

### Up-family structure
`BuffDebuffGroup 30001` contains:

- `43`
- `44`
- `45`
- `46`
- `47`

Observed fight behavior and traced skills supported the first four as the currently used sigil-resistance-up family.

### Down-family structure
`BuffDebuffGroup 40001` contains:

- `48`
- `49`
- `50`
- `51`
- `52`

Observed fight behavior and traced skills supported this as the matching sigil-resistance-down family.

## Key fight observations that drove the mapping

### `Sigil Defense`
Confirmed from footage to apply:

- Circle sigil resistance up
- X sigil resistance up

This matched the traced `BuffDebuffType` pair:

- `43`
- `45`

### `Reviving Ultima Charge`
Confirmed from footage to apply:

- Circle sigil resistance up
- Reprieve

This reinforced:

- `43` -> `Circle Sigil Resistance Up`
- `StatusChangeType 7` -> `Reprieve`

### `Ultima Charge`
A separate Ultima Weapon Majeure variant was observed applying:

- Triangle sigil resistance up

This aligned with the traced `44` usage in the `Ultima Charge` family.

### `Guard breached...`
Observed and traced to apply:

- stun
- triangle sigil resistance down
- X sigil resistance down

This aligned with:

- `49` -> `Triangle Sigil Resistance Down`
- `50` -> `X Sigil Resistance Down`

### `Renewed Guard`
Observed after the broken-state window to:

- clear the break-state package
- reapply the triangle and X sigil resistance-up effects
- clear stun / restore the charge-bar state

In data, `Renewed Guard` uses a cancel effect that removes `BuffDebuffGroup 40001`, then reapplies the up-family effects associated with the visible icons.

## Why `48` and `51` are inference rather than direct confirmation

`48` and `51` fit the family structure cleanly, but the current GL master data does not provide a simple localized label that says `Circle Sigil Resistance Down` or `Diamond Sigil Resistance Down` directly.

The accepted reasoning is:

- `43-46` map cleanly to the four currently observed sigil-up effects
- `49` and `50` were directly observed as triangle/X down effects
- `48-51` form the mirrored down-family for `43-46`
- `47` and `52` have no active `SkillBuffDebuff` rows in current GL data, making it likely they are unused extra family slots rather than currently live effects

Given that structure, the most coherent working interpretation is:

- `48` mirrors `43` -> circle down
- `51` mirrors `46` -> diamond down

## Maintenance guidance

If a future event, data update, or localized skill label provides stronger evidence:

1. Update the mapping in `WeaponSearchDataService.BuffDebuffTypes`
2. Update `WeaponSearchDataServiceTests` to match
3. Revise this note with the new evidence source
4. If `47` or `52` gain active `SkillBuffDebuff` rows, treat that as a strong signal that the reserved-slot assumption is no longer valid
