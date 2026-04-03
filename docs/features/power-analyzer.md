# Power Analyzer Deep Dive

This section documents the advanced scoring and assignment behavior used by `PowerLevelAnalyzer`.

## Entry Points
- UI page: `Pages/PowerLevelAnalyzer.cshtml`
- Page model: `Pages/PowerLevelAnalyzer.cshtml.cs`
- Analysis orchestrator: `Services/Gb20Analyzer.cs`
- Ingestion: `Services/Gb20Ingestion.cs`
- Core scoring logic: `Services/TeamOptimizer.cs`
- Guild assignment: `Services/GuildAssigner.cs`

## Input Model
- Data source: configured Google Sheet (`GoogleSheets:SurveySheets`) or uploaded CSV.
- Required column: `In-Game Name`.
- Additional used fields include:
  - `Timestamp` (dedupe latest submission)
  - `Discord Name (If different)`
  - `Your Guild`
  - `Your Time Zone`
  - `Battle release day banner?`

## Processing Pipeline
1. Ingest rows to `AccountRow` (`ItemResponsesByColumnName`).
2. Deduplicate by in-game name (latest timestamp wins).
3. Build battle context from user options:
   - enemy weakness
   - preferred damage type
   - target scenario (single/multi)
   - synergy effect bonus overrides
   - enabled team templates
4. Score best team per account using `TeamOptimizer`.
5. Assign players into guilds using `GuildAssigner` + `data/guildRules.json`.
6. Render ranked results, guild summaries, and detailed score breakdowns.

## Advanced Scoring Concepts (Proprietary/Complex)

### Team Construction
- Exactly 3 characters per team.
- Role constrained by configured templates (`TeamTemplateCatalog`).
- Non-template teams can be evaluated with a penalty.

### Role/Weapon Importance
- DPS contribution drives most score impact.
- `MaxDpsAllowed = 2` (hard-coded safeguard in optimizer).
- Weapon scoring includes OB thresholds and potency-aware bonuses.

### Potency and Off-Hand Rules
- Main-hand/Off-hand selection both score, but context-sensitive weighting applies.
- Off-hand potency can be de-weighted or zeroed when it has no relevant battle synergy.
- Element mismatch can zero potency contribution for elemental contexts.

### Utility and Side Systems
- Summons, enemy abilities, memoria, and materia contribute additive utility bonuses.
- Utility list is capped to top contributors in final score.

### Customization Rule (Important)
- Weapon customizations only unlock at `OB1+`.
- At `OB0/5★`, customization badges/details should not be counted in analyzer UI output.

## Guild Assignment Logic
- Loads `GuildRulesConfig` from `data/guildRules.json`.
- Supports:
  - locked players to fixed guild
  - per-player excluded guilds
  - ensure-players list (placeholder if missing from CSV)
- Assignment then fills guilds in ranking order with constraint checks.

## Debugging and Tuning
- Use `Show Debug` to inspect detailed breakdowns and applied rules.
- Confirm template toggles and synergy bonus overrides are reflected in `AppliedRules` output.
- If rankings look off, validate:
  - input column names
  - catalog enrichment status (`DataDiagnostics`)
  - guild rules file validity

## Common Maintenance Tasks
- Update `data/teamTemplates.json` for evolving composition strategies.
- Update `data/guildRules.json` when roster constraints change.
- Adjust synergy mappings in `SynergyDetection` and `TeamOptimizer` for new game effects.
