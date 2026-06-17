# Special Cases and Maintenance Notes

This document captures nuanced behavior that should be retained unless intentionally changed.

## Access Control Notes
- Leadership pages are path-based (`SharedAccess.ProtectedPages`).
- Cookie validity is tied to `PasswordVersion`; bump it to invalidate existing unlocks.

## Power Analyzer Notes
- Guild assignment ranks by team **score** inside `GuildAssigner` (it does not assume `rankedTeams` arrives sorted — the V2 adapter returns CSV/account order, the legacy engine pre-sorts). Locked players (`guildRules.json` → `lockedPlayers`) are placed first and intentionally override score; per-guild exclusions can also push a high scorer to a later guild. So some out-of-score-order placements are by design, but guilds otherwise fill top-down by score.
- Survey dedupe uses latest `Timestamp` per in-game name.
- Missing configured players (guild rules) can be inserted with placeholder score 0.
- Team scoring strongly favors DPS + weapon potency context; this is intentional.
- DPS off-hand handling is intentionally utility-biased rather than a second full-value damage slot.
- Team synergy bonus must be derived from the final selected weapons after re-optimization, using weighted categories plus diminishing returns.
- Outfit scoring intentionally preserves the slot rule: main outfit gets full command value, while sub outfits get no command value and only half passive/base contribution.
- Weapon customization behavior:
  - customizations unlock once a weapon reaches `Lv80+`
  - analyzer/UI surfaces should not suppress customization display solely because a weapon is `OB0`

## Guild Battle Simulation Notes
- `CurrentDay` is critical input; incorrect day skews remaining-hit and HP assumptions.
- HP override is partial: blank stage values continue using computed HP.
- `S6` override value requires `S6 Unlocked` to be meaningful.
- Simulation loop includes promotion/demotion and late-stage-priority mechanisms; these are intentional for practical battle behavior.

## Enemy Stats Notes
- Stage-based search intentionally returns multiple enemies per stage/wave context.
- Keep elemental mapping aligned with canonical IDs (regression-sensitive area).
- Keep status immunities and buff/debuff resistances separated to avoid misclassification.
- Sigil resistance backfills in `WeaponSearchDataService` are partially footage-derived; see `docs/notes/sigil-effect-mapping-inference.md` before changing `BuffDebuffType` IDs `43-52`.

## Data/Catalog Maintenance
- Keep `nameCorrections.json` synchronized with survey naming drift.
- Update `teamTemplates.json` and `guildRules.json` per event/roster cycle.
- Keep `stagePointCalibration.json` current with point-system changes.
- Monitor external FF7EC data schema changes (`external/UnknownX7`) after upstream updates. The master data (`external/UnknownX7/FF7EC-Data/MasterData/gl/Weapon.json`) is the source of truth; the `data/weaponData_*.tsv` files are stale backups only.
- **Non-obtainable duplicate weapons** are curated out via `WeaponSearchDataService.ExcludedWeaponIds`. The master data can contain multiple records with the same display name where only one is player-obtainable, and there is **no data field that marks obtainability** — it must be hand-verified against the live game.
  - Known case: "Buster Sword Origin" (Zack) — id **20033** is the real, obtainable one (Boost PDEF / Boost HP + Sigil Boost I); id **20002** is the excluded legacy duplicate. It was the only duplicate-name collision across the inventory tracking-sheet samples.
  - Caution: the `<sprite="tmp_icon" ...>` tag seen on a materia is NOT an "unreleased/placeholder" marker (it is just an unresolved Sigil-materia icon). Do not use `tmp_icon` or "lowest id" to guess which duplicate is real — both heuristics pick the wrong record here.
  - After editing `ExcludedWeaponIds`, re-run the byte-identical repro signature test (excluding a weapon changes the analyzer's candidate set); the Buster Sword Origin exclusion left it byte-identical.

## Async Analysis Job Notes
The Player Power Analyzer V2 and Power Level Analyzer run their analyses as background jobs (`AnalysisJobService` + `AnalysisJobWorker`) so each request stays under Cloudflare's 100s `524` timeout. See [Application Overview](../architecture/application-overview.md#async-background-analysis-jobs) for the design.

- **Single-instance assumption.** The job store is in-memory. It is correct only for a single app instance; behind a load balancer a status poll could hit a different instance. Scaling out requires a shared store (Redis/DB) or sticky routing.
- **Jobs do not survive a restart/deploy.** In-memory by design — a deploy mid-run loses the job; the user simply re-runs. Acceptable for now; persist if that changes.
- **Concurrency cap.** `AnalysisJobWorker.MaxConcurrentJobs = 2` protects the box. Raise only if the host can absorb more parallel heavy analyses.
- **Eviction.** Finished jobs live 30 minutes (`FinishedRetention`) and are capped at 200 (`MaxRetainedJobs`). After eviction, a `?resultJobId=` reload shows no result (graceful) and `AnalyzeStatus` returns `notfound`.
- **Export guilds CSV** exports the CURRENTLY displayed analysis, not a fresh run. `OnGetExportGuilds(resultJobId)` builds the CSV from the completed job's result bundle (`BuildGuildAssignmentsCsv`), so it (a) reflects whichever engine produced the on-screen result — V2 or legacy — and (b) is sub-second with no 524 risk. The button is a GET download link that only appears after an analysis has been run (and is disabled otherwise); if the job has expired, the page shows a "run the analysis first" hint. Earlier, the export re-ran the analysis synchronously AND always used the legacy engine regardless of the V2 checkbox — both fixed.
- **Known follow-ups (v1 was intentionally minimal, no engine changes):**
  - Form selections reset after the completion redirect (the result is correct; inputs return to defaults). Restore from the job/request if this becomes annoying.
  - Progress is elapsed-time only — no real percent or cancellation (that would require threading `IProgress`/`CancellationToken` through the engine).

## Operational Caveats
- Zelarith dispatcher path relies on external executable and file I/O contract.
- If hosted in environments with parallel usage, add process/file isolation around dispatcher execution to avoid collisions.

## Documentation Discipline
When changing scoring/simulation behavior, update:
1. `docs/features/power-analyzer.md` or `docs/features/guild-battle-simulation.md`
2. `docs/configuration/configuration-reference.md` if new knobs are added
3. root `README.md` if user-visible UI/options change
