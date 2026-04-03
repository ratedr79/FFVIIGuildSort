# Other Pages and Tools

## Gear Search (`/GearSearch`)
- Purpose: searchable gear index with rich filters and detail views.
- Data source: `WeaponSearchDataService` + catalog enrichment.
- Includes View Levels modal with dynamic OB/level snapshots.
- Customization unlock note: customizations are unavailable at OB0 (5★), unlock at OB1.

## Enemy Stats (`/EnemyStats`)
- Purpose: search by boss or stage and inspect enemy details.
- Stage-name match behavior:
  - `Stage` column shows stage
  - `Name` column shows enemy
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
