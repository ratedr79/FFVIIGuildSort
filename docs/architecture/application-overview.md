# Application Overview

`FFVIIEverCrisisAnalyzer` is a multi-tool web application for SOLDIER guild operations.

## High-Level Flows

### 1) Power Analyzer Flow
1. Read survey CSV/Google Sheet (`Gb20Ingestion`).
2. Parse account responses into `AccountRow`.
3. Evaluate best team per account (`TeamOptimizer` via `Gb20Analyzer`).
4. Apply guild placement rules (`GuildAssigner`).
5. Render rankings and breakdowns (`PowerLevelAnalyzer` page).

### 2) Guild Battle Flow
1. Read guild battle CSV (`GuildBattleParser`).
2. Build current state (`TodayStateBuilder`).
3. Optionally override stage HP values from UI.
4. Simulate with assignment engine (`GuildBattleAssignmentEngine`).
5. Render stage groups, HP projections, and attack log.

### 3) Guild Battle Test Flow
1. Same parse/state setup as Guild Battle.
2. Execute many runs via `SimulationHarness`.
3. Aggregate metrics and score best/worst runs.
4. Export summary as CSV/JSON.

### 4) Zelarith Assignment Flow
1. Prepare TSV input range for external dispatcher.
2. Write to Dispatcher folder and run `Dispatcher.exe`.
3. Parse dispatcher output into `DispatcherParsedPlan`.
4. Simulate fixed assignments repeatedly and aggregate results.

## Access Control
- `SharedAccessGate` middleware checks route membership in configured protected pages.
- Access token is a signed cookie tied to password hash and version.
- Unlock page: `/Unlock`.

## Data and Catalogs
- Gear and enemy logic rely on local files in `data/` and external FF7EC data under `external/`.
- Weapon and enemy catalogs are singleton services and are loaded once per app lifecycle.

## Important Couplings
- `PowerLevelAnalyzer` depends on survey column naming conventions.
- `StagePointEstimator` depends on `data/stagePointCalibration.json` being present in output.
- Zelarith dispatcher flow depends on external executable/file protocol.
