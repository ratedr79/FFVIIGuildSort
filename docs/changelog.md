# Changelog

All notable changes to this project should be documented in this file.

## Format
- Keep entries under `Unreleased` until deployment.
- Move completed items into a dated release section like `## 2026-04-02`.
- Use short bullets under these headings when possible:
  - `Added`
  - `Changed`
  - `Fixed`
  - `Docs`

---

## Unreleased

### Added
- Developer documentation set under `docs/`:
  - `docs/getting-started/onboarding.md`
  - `docs/architecture/application-overview.md`
  - `docs/configuration/configuration-reference.md`
  - `docs/features/power-analyzer.md`
  - `docs/features/guild-battle-simulation.md`
  - `docs/features/other-pages.md`
  - `docs/notes/special-cases-and-maintenance.md`
  - `docs/README.md`
- Initial changelog scaffold (`docs/changelog.md`).
- Gear Search now resolves passive-skill (`R Ability`) effects from FF7EC passive tables using OB/level point totals, including support for named passives that map to multiple effects.
- Gear Search advanced filters now include a `Sub-R Abilities` panel (normalized passive-effect categories) with in-panel search, clear, and `No matches found.` feedback.
- Added a new public `Should I Attack?` page to analyze a selected player from configured guild sheets and recommend `Attack now` vs `Hold` with simulation evidence.
- Added a leadership-only `Should I Attack Bulk Diagnostics` page for single-sheet batch recommendation runs and aggregate alerting.
- Added a new `FFVIIEverCrisisAnalyzer.Tests` xUnit project with orchestration-focused tests for `Should I Attack` tie-break, horizon, queue-priority, fallback, and immediate-mode behavior.
- Added a new public `Support Team Builder` page backed by local UnknownX7 JSON data (`external/UnknownX7/FF7EC-Data`) with multi-effect filters, range/potency thresholds, max-character constraints, must-have/exclude character filters, and ranked team output.
- Added `SupportTeamBuilderService` for effect matching, team assignment generation, duplicate filtering, and rank ordering aligned with support-builder precedence.
- Added browser-local persistence (`support-team-builder-state-v1`) for per-weapon owned OB selections used by the support-team workflow.
- Added leadership-triggered full UnknownX7 data reload support in `Data Diagnostics` via `WeaponSearchDataService.ReloadData()`.
- Added integration-style tests for support-team-builder option loading and max-character assignment constraints.

### Changed
- End-user `README.md` rewritten with:
  - table of contents
  - per-tool walkthroughs
  - UI input reference tables
  - configuration and accuracy notes
- Gear Search advanced filters now include in-panel search + clear controls for long `R Abilities` and `Effects` lists, with `No matches found.` feedback while preserving selected checkbox state.
- Gear Search table `R Abilities` now expose hover details showing resolved OB10/Lv130 passive effects.
- Gear Search `R Ability` effect details now use tap/click-friendly popovers on mobile/touch devices (with outside-tap dismiss), while preserving hover/focus behavior on desktop.
- Gear Search View Levels modal now renders `R Abilities` in two-line format (`+points/name` plus one-or-more resolved effect lines) that updates as OB/level changes.
- Passive `R Ability` effect percentage rendering now supports `PassiveSkillType`-specific coefficient scaling (including `Type 8` resist-style values), fixing over-scaled percentage output.
- Gear Search results now render as cards across mobile and desktop (desktop uses a multi-column card layout), while preserving existing filter/search/compare interactions.
- Gear Search card behaviors were refined for readability and interaction consistency: desktop shows inline `R Ability` effect detail while mobile keeps tap/click-friendly affordances.
- Gear Search customization-added `R Abilities` now display `+points`, include resolved effect text in card/compare views, and participate in advanced `Sub-R Abilities` filtering.
- Gear Search View Levels modal now surfaces customization-added `R Abilities` inside the `R Abilities` section using the same point/effect rendering pattern as base `R Abilities`.
- Gear Search View Levels modal now adds a `Cust.` badge to customization-added `R Abilities` so optional unlock passives are clearly identified.
- Enemy Stats results now render as cards (single-column on smaller screens, 3-column desktop), with `Show Details` opening a modal instead of expanding inline.
- Enemy Stats search suggestions now use a custom mobile-friendly typeahead dropdown with outside-click dismiss and `Escape` close behavior.
- Guild battle dispatcher execution is now wrapped in a synchronized runner service so concurrent requests do not corrupt shared `input.txt`, and `Should I Attack` paths automatically fall back to engine-based simulation when dispatcher execution fails.
- `Should I Attack` now runs `25` simulations per analysis, exposes dispatcher/fallback run source in recommendation evidence, and applies mode-specific run selection:
  - standard mode: earliest selected-player attack, then resets, then final HP sum
  - immediate mode: strict first non-reset selected-player hit when available, then clears, final HP sum, and points
- Immediate-use recommendations now prefer a run-aligned stage (selected run's first selected-player attack stage) and expose heuristic fallback stage/source when run-aligned stage is unavailable.
- `WeaponSearchDataService` now tracks reload metadata (`LastLoadedUtc`, `ReloadCount`) and supports synchronized in-process full data reload.
- Support Team Builder matching-weapon `Owned OB` changes now auto-refresh ranked team results by auto-submitting search on selector change.
- Support Team Builder now includes a top-level external-link action to the original reference builder and a visible beta-status notice.
- Support Team Builder ranked team rows now allow opening weapon details modal from both main-hand and off-hand weapon names.
- Data Diagnostics reload flow now performs post-reload catalog re-enrichment (`WeaponCatalog.RefreshFromGearSearch()`) after `WeaponSearchDataService.ReloadData()`.
- Support Team Builder now includes outfit-aware matching and ranking: per-filter matching outfit results, owned/not-owned outfit state, one-outfit-per-character assignment, outfit-inclusive potency scoring, and duplicate filtering by combined weapon/outfit composition.
- Support Team Builder ranked-team rows now render selected outfit names after weapons when present.
- Support Team Builder weapon-details modal now includes customization details beneath ability text.

### Fixed
- Weapon customization unlock behavior now enforces `OB1+` in simulation/UI surfaces:
  - snapshot customization generation suppressed at `OB0`
  - power analyzer customization indicators suppressed at `OB0`
- Gear Search snapshot modal now shows `Customizations unlock at OB1.` hint when `OB0` is selected.

### Docs
- Captured advanced logic and maintenance notes for:
  - Power Analyzer scoring and guild assignment behavior
  - Guild Battle simulation, aggregation, and dispatcher integration
  - configuration-driven behavior and operational caveats
- Added a direct deep-dive section for Power Analyzer synergy processing and effect weighting in `docs/features/power-analyzer.md#synergy-logic-and-effect-processing`.
- Added `Data Diagnostics` visibility for distinct `PassiveSkillType` values with representative sample outputs to help validate passive effect scaling behavior.
- Updated end-user `README.md` walkthrough/input sections for Gear Search and Enemy Stats to reflect card-based results, modal details flow, and Enemy Stats typeahead usage.
- Updated `docs/features/other-pages.md` entries for Gear Search and Enemy Stats to reflect current UI behavior.
- Added end-user `README.md` documentation for `Should I Attack` and leadership `Should I Attack Bulk Diagnostics`, including a quick stats section for simulation/run-selection behavior.
- Updated `docs/features/other-pages.md` with developer-facing details for `Should I Attack` orchestration, immediate-mode strict first-hit behavior, run-aligned stage recommendation, and bulk diagnostics intent.
- Added end-user `README.md` documentation for `Support Team Builder` inputs and walkthrough, plus `Data Diagnostics` reload behavior.
- Updated `docs/features/other-pages.md` with `Support Team Builder` architecture/data-flow details and leadership reload diagnostics behavior.
- Updated `README.md` Support Team Builder section with a user-facing quick-start and current UI behavior notes (beta banner, original-tool link, automatic ranking refresh on Owned OB changes).
- Updated `docs/features/other-pages.md` with technical behavior details for Support Team Builder modal entry points, auto-refresh interactions, and Data Diagnostics post-reload re-enrichment.
- Updated `README.md` Support Team Builder walkthrough/inputs to document outfit ownership controls, ranked outfit display, and weapon modal customization details.
- Updated `docs/features/other-pages.md` Support Team Builder internals to cover outfit assignment/scoring/dedupe logic and customization display in weapon details modal.
