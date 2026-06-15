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

## Async Background Analysis Jobs
Long analyses (Player Power Analyzer V2 in Full/Pro mode, and the whole-player-base Power Level Analyzer) can run for minutes. Production sits behind a Cloudflare proxy whose **100s origin-response timeout (error 524)** cannot be raised without Enterprise licensing. Because 524 is a *time-to-first-byte* limit (not a total-duration cap), the fix is to keep every HTTP request sub-second by moving the computation off the request thread.

- `Services/AnalysisJobService.cs` — singleton in-memory job registry. `Enqueue(work)` returns a job id immediately; tracks `Status` (Queued/Running/Completed/Failed), elapsed time, result, and error. Finished jobs are evicted after 30 minutes (and capped at 200) to bound memory.
- `Services/AnalysisJobWorker.cs` — a `BackgroundService` that drains a `Channel` and runs each job in its **own DI scope** (the originating request scope is gone by then, so jobs must resolve the scoped analyzer services themselves). Bounded to 2 concurrent heavy jobs; the rest queue.
- Both are registered in `Program.cs` (`AddSingleton<AnalysisJobService>()` + `AddHostedService<AnalysisJobWorker>()`).

Per-page request flow (Player Power Analyzer V2 and Power Level Analyzer):
1. Client intercepts the submit, `POST ?handler=Start*` → enqueues a job, returns `{ jobId }` (sub-second).
2. Client polls `GET ?handler=AnalyzeStatus&id=` every ~1.5–2s (sub-second each), showing an elapsed-time overlay.
3. On `completed`, the client **redirects to `?resultJobId=<id>`**; `OnGet(resultJobId)` pulls the precomputed result from the registry and renders the full page server-side (sub-second, no recompute). This reuses all existing server-rendered result markup and interactivity unchanged.

The original synchronous handlers (`OnPostAnalyze` / `OnPostAsync`) are retained as no-JS fallbacks. The Power Level Analyzer keeps its heavy logic in the page model as `ComputeAsync(byte[])`; the job runs it on a fresh page-model instance via `ActivatorUtilities.CreateInstance<PowerLevelAnalyzerModel>(scope)` and returns a `PowerLevelAnalysisResult` bundle that `OnGet` restores. See [Special Cases and Maintenance Notes](../notes/special-cases-and-maintenance.md#async-analysis-job-notes) for operational caveats.

## Important Couplings
- `PowerLevelAnalyzer` depends on survey column naming conventions.
- `StagePointEstimator` depends on `data/stagePointCalibration.json` being present in output.
- Zelarith dispatcher flow depends on external executable/file protocol.
