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

### Changed
- End-user `README.md` rewritten with:
  - table of contents
  - per-tool walkthroughs
  - UI input reference tables
  - configuration and accuracy notes

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
