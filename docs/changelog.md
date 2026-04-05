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
