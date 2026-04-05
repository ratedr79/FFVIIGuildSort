# Other Pages and Tools

## Gear Search (`/GearSearch`)
- Purpose: searchable gear index with rich filters and detail views.
- Data source: `WeaponSearchDataService` + catalog enrichment.
- Results UI uses card-based rendering across mobile and desktop breakpoints.
- Includes View Levels modal with dynamic OB/level snapshots.
- Customization unlock note: customizations are unavailable at OB0 (5★), unlock at OB1.
- Passive-skill (`R Ability`) effects are resolved from FF7EC passive data tables using current passive points:
  - `SkillPassive` → `SkillPassiveLevel` (point-to-level mapping)
  - `SkillPassiveEffectGroup` → `SkillPassiveEffectLevel` (level-specific effect values)
  - localized description templates (`Localization/en.json`) with value/coefficient substitution
- Desktop cards now show resolved `R Ability` effect details inline; mobile/touch behavior keeps tap/click-friendly affordances.
- View Levels modal now renders each `R Ability` as:
  - first line: `+points SkillName`
  - subsequent lines: one-or-more resolved passive effect descriptions
- Advanced Search includes `Sub-R Abilities` filter options built from normalized passive-effect categories.
- Sub-R filtering is semantic key matching (not substring matching), so similarly named effects (for example `HP` vs `HP Gain`) remain distinct.

## Enemy Stats (`/EnemyStats`)
- Purpose: search by boss or stage and inspect enemy details.
- Results render as cards (single-column on smaller screens, 3-column on desktop).
- `Show Details` opens a modal so details do not shift page layout inline.
- Search suggestions use a custom typeahead dropdown optimized for mobile interactions.
- Stage-name match behavior:
  - `Stage` field shows stage
  - `Name` field shows enemy
  - `Level` shows enemy level
- Enemy-name match behavior keeps stage as `N/A`.
- Immunities panel is status-effect-focused; buff/debuff resistance is separate.

## Data Diagnostics (`/DataDiagnostics`) [Leadership]
- Purpose: validate whether weapon/costume entries are enriched from GearSearch data.
- Helps identify missing enrichment/potency/R-ability data.

## Unlock (`/Unlock`)
- Shared password gate for leadership-only pages.
- Creates signed unlock cookie valid for configured duration.

## Home (`/`)
- Card index of all major tools.
- Indicates leadership-only pages with lock icons.

## Legacy Utility Service
- `CsvProcessor` and `PowerLevelCalculator` exist as generic CSV/power utilities.
- Core production ranking logic currently runs through GB20 ingestion + `TeamOptimizer` path.
