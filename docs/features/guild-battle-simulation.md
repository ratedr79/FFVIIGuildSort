# Guild Battle Simulation Deep Dive

This section documents simulation behavior across `GuildBattle`, `GuildBattleTest`, and `ZelarithAssignment`.

## Primary Components
- Parser: `Services/GuildBattleParser.cs`
- State builder: `Services/TodayStateBuilder.cs`
- Assignment/simulation engine: `Services/GuildBattleAssignmentEngine.cs`
- Multi-run harness: `Services/SimulationHarness.cs`
- Point estimator: `Services/StagePointEstimator.cs`
- Models: `Models/GuildBattleModels.cs`, `Models/SimulationTestModels.cs`

## Data and State Construction

### Parsing
`GuildBattleParser` reads the guild battle CSV and maps:
- stage columns (C-H)
- per-player mock percentages
- attempt logs (day/stage/percent/kill)

### Today State
`TodayStateBuilder` computes current battle state from historical attempts:
- remaining HP by stage
- remaining hits for day
- stage unlock state (including S6 logic)
- debug replay/diagnostic info in aggregate mode

Build mode used in pages is primarily `OrderAgnosticAggregate`.

## Core Simulation (`GuildBattleAssignmentEngine`)

### GenerateBattlePlan
- Builds effective player damage values (average minus smart margin).
- Creates initial stage assignments with explicit prioritization strategy.
- Handles S6 reservation and unlock assumptions.
- Uses simulation loop to produce expected resets, attack logs, final HP, and stage clears.

### SimulateWithFixedAssignments
- Used with dispatcher outputs and synthetic plans.
- Resolves per-stage attack budgets (including `Attacks == 0` as all remaining).
- Supports dynamic stage promotion/demotion and cleanup logic.
- Tracks and logs:
  - promotions
  - cleanup promotions
  - late-stage-priority mode
  - resets and attacks

## Multi-Run Aggregation (`SimulationHarness`)
- Runs N simulations with seed strategy (`Auto`, `Fixed`, `Incremental`).
- Aggregates reset/clear/final-HP statistics.
- Computes best/worst run by score mode:
  - `MinSumFinalHP`
  - `MinS6FinalHP`
  - `MaxTotalClears`
- Supports filter toggles that recompute player averages:
  - outlier filter
  - deviation cap

## Stage Point Estimation
`StagePointEstimator` loads `data/stagePointCalibration.json`:
- interpolates stage points from calibration points
- calculates threshold bonus points (75/50/25/0 remaining HP crossings)

This estimator influences point-related log outputs and summary metrics.

## Zelarith/Dispatcher Path (Special)
- Page: `Pages/ZelarithAssignment.cshtml.cs`
- Uses `Dispatcher:Path` and external `Dispatcher.exe`.
- Writes TSV (`input.txt`) and parses stdout into `DispatcherParsedPlan`.
- Re-simulates parsed assignments in multiple runs.

### Operational Risk
Dispatcher uses shared file-based protocol and external process execution; concurrent calls can conflict without isolation/serialization.

## Important Special-Case Behavior
- HP override can replace computed stage HPs at runtime.
- S6 HP override should only be applied if `S6 Unlocked` is checked.
- Wrong `Current Day` selection will materially change remaining attempts and stage assumptions.

## Troubleshooting Checklist
- Validate CSV structure and stage headers.
- Check day selection and HP override values.
- Confirm calibration file is present and valid.
- Inspect attack log events for promotions/demotions/late-stage mode.
- Compare single-run (`GuildBattle`) vs aggregate trends (`GuildBattleTest`).
