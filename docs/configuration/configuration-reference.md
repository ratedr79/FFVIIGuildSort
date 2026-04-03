# Configuration Reference

This page documents app settings and data files required to drive the application.

## `appsettings.json`

### `Logging`
- Standard ASP.NET logging levels.

### `AllowedHosts`
- Standard ASP.NET host filtering (`*` by default).

### `Dispatcher`
- `Path`: absolute folder path containing `Dispatcher.exe` and writable `input.txt`.
- Used by `ZelarithAssignment` page.

### `SharedAccess`
Used by `SharedAccessGate`:
- `PasswordHash`: base64 SHA-256 hash for unlock password.
- `PasswordVersion`: invalidate existing unlock cookies by changing this.
- `CookieName`: unlock cookie name (default `LeadershipUnlock`).
- `UnlockDurationHours`: cookie validity period.
- `ProtectedPages`: exact paths requiring unlock.

### `GoogleSheets`
- `SurveySheets`: options shown in Power Analyzer sheet dropdown.
- `GuildBattleSheets`: options shown in Guild Battle / Test / Zelarith dropdowns.
- Each entry is `Name` + published CSV `Url`.

## Data Files in `data/`

### Runtime-Critical
- `stagePointCalibration.json`
  - Stage point calibration points (percent -> points) and bonus values.
  - Consumed by `StagePointEstimator`.
- `enemyAbilities.json`
  - Enemy ability metadata used by enemy-related logic.

### Operational/Feature Data
- `teamTemplates.json`: valid team role templates and priority.
- `guildRules.json`: lock/exclusion/ensure-player rules for guild assignment.
- `nameCorrections.json`: correction maps for weapon/outfit names.
- `summons.json`, `memoria.json`: utility catalog data.
- `weaponData*.tsv`, `weaponFallback.json`: gear-related data support.
- historical CSV snapshots for simulation/debugging.

## External Data
- `external/UnknownX7/FF7EC-Data` is used by catalog/services for rich gear/enemy data.
- Missing/changed upstream files can affect Gear Search/Enemy Stats behavior.

## Build Copy Behavior
From project file:
- `data/enemyAbilities.json` and `data/stagePointCalibration.json` are copied `Always`.
- `data/weaponData.tsv` is copied `Always`.
- External TSV copy is `PreserveNewest`.

## Recommended Ops Practices
- Keep Google Sheet URLs up to date for each guild battle cycle.
- Version password using `PasswordVersion` when leadership password rotates.
- Update calibration points as battle scoring changes.
- Keep Dispatcher path valid on host environments that use Zelarith workflow.
