# Special Cases and Maintenance Notes

This document captures nuanced behavior that should be retained unless intentionally changed.

## Access Control Notes
- Leadership pages are path-based (`SharedAccess.ProtectedPages`).
- Cookie validity is tied to `PasswordVersion`; bump it to invalidate existing unlocks.

## Power Analyzer Notes
- Survey dedupe uses latest `Timestamp` per in-game name.
- Missing configured players (guild rules) can be inserted with placeholder score 0.
- Team scoring strongly favors DPS + weapon potency context; this is intentional.
- OB customization behavior:
  - customizations should only count/display at `OB1+`
  - OB0 customization display in analyzer should remain suppressed

## Guild Battle Simulation Notes
- `CurrentDay` is critical input; incorrect day skews remaining-hit and HP assumptions.
- HP override is partial: blank stage values continue using computed HP.
- `S6` override value requires `S6 Unlocked` to be meaningful.
- Simulation loop includes promotion/demotion and late-stage-priority mechanisms; these are intentional for practical battle behavior.

## Enemy Stats Notes
- Stage-based search intentionally returns multiple enemies per stage/wave context.
- Keep elemental mapping aligned with canonical IDs (regression-sensitive area).
- Keep status immunities and buff/debuff resistances separated to avoid misclassification.

## Data/Catalog Maintenance
- Keep `nameCorrections.json` synchronized with survey naming drift.
- Update `teamTemplates.json` and `guildRules.json` per event/roster cycle.
- Keep `stagePointCalibration.json` current with point-system changes.
- Monitor external FF7EC data schema changes (`external/UnknownX7`) after upstream updates.

## Operational Caveats
- Zelarith dispatcher path relies on external executable and file I/O contract.
- If hosted in environments with parallel usage, add process/file isolation around dispatcher execution to avoid collisions.

## Documentation Discipline
When changing scoring/simulation behavior, update:
1. `docs/features/power-analyzer.md` or `docs/features/guild-battle-simulation.md`
2. `docs/configuration/configuration-reference.md` if new knobs are added
3. root `README.md` if user-visible UI/options change
