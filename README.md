# SOLDIER Tools

A web toolkit for Final Fantasy VII Ever Crisis guild operations: team ranking, guild battle simulations, assignment testing, gear lookup, and enemy data lookup.

## Table of Contents
- [Getting Started](#getting-started)
- [Access and Leadership Tools](#access-and-leadership-tools)
- [Home Page](#home-page)
- [Power Level Analyzer](#power-level-analyzer)
  - [Power Level Analyzer: What It Does](#power-level-analyzer-what-it-does)
  - [Power Level Analyzer: Inputs](#power-level-analyzer-inputs)
  - [Power Level Analyzer: Basic Steps](#power-level-analyzer-basic-steps)
- [Guild Battle](#guild-battle)
  - [Guild Battle: What It Does](#guild-battle-what-it-does)
  - [Guild Battle: Inputs](#guild-battle-inputs)
  - [Guild Battle: Basic Steps](#guild-battle-basic-steps)
- [Guild Battle Test](#guild-battle-test)
  - [Guild Battle Test: What It Does](#guild-battle-test-what-it-does)
  - [Guild Battle Test: Inputs](#guild-battle-test-inputs)
  - [Guild Battle Test: Basic Steps](#guild-battle-test-basic-steps)
- [Zelarith Assignment](#zelarith-assignment)
  - [Zelarith Assignment: What It Does](#zelarith-assignment-what-it-does)
  - [Zelarith Assignment: Inputs](#zelarith-assignment-inputs)
  - [Zelarith Assignment: Basic Steps](#zelarith-assignment-basic-steps)
- [Gear Search](#gear-search)
  - [Gear Search: What It Does](#gear-search-what-it-does)
  - [Gear Search: Inputs](#gear-search-inputs)
- [Enemy Stats](#enemy-stats)
  - [Enemy Stats: What It Does](#enemy-stats-what-it-does)
  - [Enemy Stats: Inputs](#enemy-stats-inputs)
- [Data Diagnostics](#data-diagnostics)
- [Configuration Needed](#configuration-needed)
- [Important Accuracy Notes](#important-accuracy-notes)

## Getting Started
1. Open the app and go to `/`.
2. Pick a tool card based on your task.
3. Use a configured Google Sheet URL or upload a CSV where supported.
4. Review warnings and summary panels before acting on recommendations.

## Access and Leadership Tools
Some tools are leadership-only and require unlock through `/Unlock`:
- Power Level Analyzer
- Data Diagnostics
- Guild Battle
- Guild Battle Test
- Zelarith Assignment

Unlock uses a shared password and a temporary cookie session.

## Home Page
The Home page lists all available tools with short descriptions and lock icons for restricted pages.

---

## Power Level Analyzer
### Power Level Analyzer: What It Does
Builds and scores 3-character teams from guild survey data, ranks players, and assigns players to guilds using configured rules.

### Power Level Analyzer: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Survey Sheet (Published CSV) | One configured `GoogleSheets:SurveySheets` option | Pulls live survey responses |
| Or Upload CSV File | `.csv` export of survey | Offline/snapshot analysis |
| Enemy Weakness | None/Earth/Fire/Ice/Lightning/Water/Wind/Holy/Dark | Adjusts elemental scoring context |
| Preferred Damage Type | Any/Physical/Magical | Biases scoring toward battle damage type |
| Enemy Count Weighting | Unknown/Single Enemy/Multiple Enemies | Influences single-target vs AoE priorities |
| Show debug details | Checked/unchecked | Expands deep scoring breakdowns |
| Team template checkboxes | Enable one or more templates | Limits valid team composition patterns |
| Synergy effect bonus selects | 0% to +500% per effect | Applies extra weight to selected buffs/debuffs |

### Power Level Analyzer: Basic Steps
1. Choose sheet or upload CSV.
2. Set weakness/type/scenario.
3. (Optional) tune templates and synergy boosts.
4. Click `Find Best Teams`.
5. Review ranked teams and details.
6. Use `Export guilds CSV` for distribution.

---

## Guild Battle
### Guild Battle: What It Does
Runs a single guild battle simulation from guild battle logs and outputs current stage HP assumptions, assignments, and attack log projections.

### Guild Battle: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Google Sheets (Published CSV) | One configured `GoogleSheets:GuildBattleSheets` option | Pulls current guild battle sheet |
| Upload CSV File | `.csv` battle log | Snapshot analysis |
| Current Day | Day 1/2/3 | Determines attempts remaining and state interpretation |
| Margin % | Number (0-25, typically low single digits) | Adds variability to effective damage |
| Overshoot % | Number (0-100) | Trigger for cleanup/promotion behavior |
| Cleanup Buffer % | Number (0-100) | Safety buffer for cleanup candidate selection |
| Seed | Optional integer | Reproduce a specific random run |
| Enable HP Override | Checkbox | Enables manual HP entry fields |
| HP S1-S6 | 0-100 or blank | Overrides computed stage HP (blank = compute from CSV) |
| S6 Unlocked | Checkbox | Applies S6 override/availability when checked |

### Guild Battle: Basic Steps
1. Choose sheet/upload and day.
2. Set margin/overshoot/cleanup settings.
3. (Optional) set HP overrides.
4. Click play button.
5. Review summary, assignments, final HP, and attack log.
6. Export assignment CSV if needed.

---

## Guild Battle Test
### Guild Battle Test: What It Does
Runs many simulations and aggregates variance metrics (resets, clear rates, final HP, assignment frequency).

### Guild Battle Test: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Google Sheet / Upload CSV | Same as Guild Battle | Source battle data |
| Current Day | Day 1/2/3 | Day-state interpretation |
| Margin (%) | Number | Simulation variance margin |
| Overshoot % | Number | Cleanup promotion trigger |
| Cleanup Buffer % | Number | Cleanup safety buffer |
| Number of Runs | 1-50 | Number of simulation runs |
| Seed Mode | Auto/Fixed/Incremental | Controls random seed behavior |
| Seed Value | Integer | Base seed for Fixed/Incremental |
| Score By | MinSumFinalHP / MinS6FinalHP / MaxTotalClears | Best run selection mode |
| Outlier Filter | Checkbox | Filters very low kill outliers |
| Deviation Cap | Checkbox | Caps average degradation vs mock |
| HP Override + S1-S6 + S6 Unlocked | Same as Guild Battle | Manual stage-state override |

### Guild Battle Test: Basic Steps
1. Load sheet/CSV and set run parameters.
2. Run multi or single detailed mode.
3. Compare aggregate metrics and best/worst run.
4. Export CSV/JSON summaries if needed.

---

## Zelarith Assignment
### Zelarith Assignment: What It Does
Uses external `Dispatcher.exe` assignment output, then re-simulates the generated plan for expected outcomes.

### Zelarith Assignment: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Google Sheets (Published CSV) | Configured guild battle sheet | Source data for dispatcher + simulation |
| Or Upload CSV File | `.csv` | Snapshot data source |
| Day | Day 1/2/3 | Day-state context |
| Margin % | Number | Damage margin |
| Runs | 1-50 | Number of simulation runs |
| Overshoot Trigger % | Number | Cleanup trigger |
| Cleanup Confidence Buffer % | Number | Cleanup confidence threshold |
| Enable HP Override + S1-S6 + S6 Unlocked | Same pattern as other GB pages | Manual state correction |

### Zelarith Assignment: Basic Steps
1. Ensure `Dispatcher:Path` points to valid dispatcher folder.
2. Select data source and options.
3. Click `Run Dispatcher`.
4. Review dispatcher output, parsed assignments, and simulation aggregate results.

---

## Gear Search
### Gear Search: What It Does
Provides searchable/filterable weapon/costume data with ability details, R abilities, sigils, compare mode, and level/OB snapshots.

### Gear Search: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Character checkboxes | One or more characters | Character filtering |
| Search | Free text | Name/ability/effect search |
| Rows per page | 25/50/100/200 | DataTable paging |
| Advanced filter checkboxes | Element, ability, range, equipment, effects, R abilities, sigils, etc. | Narrow results |
| Has Customizations | Checkbox | Show only entries with customization data |
| View Levels modal Overboost | 5★ (OB0) to OB10 | Snapshot at specific overboost |
| View Levels modal Level | 1-130 (slider/number) | Snapshot at specific weapon level |

### Important UI Note
- Customizations unlock at `OB1+`.
- At `5★/OB0`, customizations are intentionally unavailable.

---

## Enemy Stats
### Enemy Stats: What It Does
Searches enemy and stage names and displays detailed boss metadata: stats, resistances, immunities, and text descriptions.

### Enemy Stats: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Boss or stage name | Free text | Finds enemy-name matches and stage-name matches |
| Search button | Click | Runs query |
| Show Details button | Per row | Expands detailed enemy panel |

### Result Behavior
- Enemy-name match: Stage shows `N/A`.
- Stage-name match: Stage column shows stage name; name/level show the matching enemy and level.

---

## Data Diagnostics
Leadership page used to verify gear enrichment status and detect missing weapon/costume enrichment data.

No user input fields are required; open page and review generated lists.

---

## Configuration Needed
The app is driven by `appsettings.json` and data files:

- `GoogleSheets:SurveySheets` for Power Analyzer sheet options.
- `GoogleSheets:GuildBattleSheets` for battle simulation sheet options.
- `SharedAccess` for protected-page unlock behavior.
- `Dispatcher:Path` for Zelarith workflow.
- `data/stagePointCalibration.json` for stage point estimation.
- `data/teamTemplates.json`, `data/guildRules.json`, `data/nameCorrections.json` for scoring and assignment controls.

---

## Important Accuracy Notes
- All analyzers depend on spreadsheet data quality.
- Missing/late/incorrect entries can produce inaccurate recommendations.
- Day selection and HP overrides strongly affect guild battle simulation output.
- Always sanity-check results against current in-game state before acting.

For developer-focused internals and maintenance details, see `docs/README.md`.
For recent documentation and behavior updates, see `docs/changelog.md`.
