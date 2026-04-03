# Onboarding

This guide helps a developer pick up the project quickly and safely.

## Stack
- ASP.NET Core Razor Pages (`net8.0`)
- `CsvHelper` for CSV ingestion
- Bootstrap + DataTables on UI pages
- JSON and TSV data files under `data/` and `external/`

## Project Layout
- `Pages/`: Razor pages (UI + page handlers)
- `Services/`: core business logic and simulation engines
- `Models/`: domain and view models
- `data/`: local config/data artifacts used by runtime logic
- `external/`: external FF7EC data sources and assets

## Startup and DI
- Entry point: `Program.cs`
- Core registrations include:
  - Power analyzer path: `Gb20Ingestion`, `TeamOptimizer`, `Gb20Analyzer`, `GuildAssigner`
  - Gear/enemy catalogs: `WeaponCatalog`, `WeaponSearchDataService`, `EnemyCatalog`, etc.
  - Guild battle scoring: `StagePointEstimator`
  - Shared leadership gate: `SharedAccessGate`

## Run Locally
1. Ensure .NET SDK for `net8.0` is installed.
2. From `FFVIIEverCrisisAnalyzer/`, run `dotnet run`.
3. Open the local URL printed by ASP.NET.
4. Leadership pages require unlock (see configuration docs).

## Core Runtime Dependencies
- `data/stagePointCalibration.json` (copied to output; required for point estimation)
- `data/enemyAbilities.json` (copied to output)
- `data/teamTemplates.json`, `data/guildRules.json`, `data/nameCorrections.json` (optional but important)
- External FF7EC master/localization data under `external/UnknownX7/FF7EC-Data`
- `Dispatcher.exe` path for Zelarith assignment workflow (config-driven)

## First Debug Targets
- Power ranking flow: `Pages/PowerLevelAnalyzer.cshtml.cs`
- Single guild battle simulation: `Pages/GuildBattle.cshtml.cs`
- Multi-run simulation harness: `Pages/GuildBattleTest.cshtml.cs` + `Services/SimulationHarness.cs`
- Dispatcher-integration flow: `Pages/ZelarithAssignment.cshtml.cs`

## Contribution Guidelines (Project-Specific)
- Prefer minimal and surgical changes to avoid simulation regressions.
- Reuse existing service logic over duplicating it in pages.
- Preserve behavior around stage assignment/simulation unless intentionally changing game logic.
- Keep developer docs up to date when adding new simulation knobs or scoring rules.
