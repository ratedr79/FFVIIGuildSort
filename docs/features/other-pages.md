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

## Should I Attack (`/ShouldIAttack`)
- Purpose: recommend `Attack now` vs `Hold` for one selected player using simulation evidence.
- Data source: selected guild sheet from `GoogleSheets:GuildBattleSheets`.
- Day handling:
  - auto-detected suggestion based on attempt history and remaining hit logic
  - user-overridable day input (`Day 1-3`)
- Run orchestration:
  - executes `25` simulations per analysis
  - dispatcher path is attempted first; engine fallback is used when dispatcher is unavailable/fails
  - selected player is prioritized in dispatcher stage assignments and player list ordering before simulation
- Recommendation behavior:
  - standard mode: run selected by earliest selected-player attack, then resets desc, then final HP sum asc
  - immediate mode: prefers runs where selected player is first non-reset attack when available, otherwise falls back to all-run ranking by clears desc, final HP sum asc, points desc
  - immediate recommendation stage uses selected run's first player attack stage when available (run-aligned), with heuristic fallback stage retained for transparency
- UI behavior:
  - confirmation modal before analysis submit
  - player-loading indicator after guild selection
  - recommendation summary includes strict first-hit status, stage-hit summary, run source (dispatcher/fallback), and simulation caveat warning

## Should I Attack Bulk Diagnostics (`/ShouldIAttackBulk`) [Leadership]
- Purpose: leadership batch diagnostics for a selected guild sheet across all parsed players.
- Produces per-player recommendation rows and aggregate counts (attack now, fallback usage, missing recommendations).
- Intended for validation and operational review rather than direct end-user decision flow.

## Unlock (`/Unlock`)
- Shared password gate for leadership-only pages.
- Creates signed unlock cookie valid for configured duration.

## Home (`/`)
- Card index of all major tools.
- Indicates leadership-only pages with lock icons.

## Legacy Utility Service
- `CsvProcessor` and `PowerLevelCalculator` exist as generic CSV/power utilities.
- Core production ranking logic currently runs through GB20 ingestion + `TeamOptimizer` path.
