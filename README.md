# SOLDIER Tools

A web toolkit for Final Fantasy VII Ever Crisis guild operations: team ranking, guild battle simulations, assignment testing, gear lookup, and enemy data lookup.

## Table of Contents
- [Getting Started](#getting-started)
- [Access and Leadership Tools](#access-and-leadership-tools)
- [Home Page](#home-page)
- [Power Level Analyzer](#power-level-analyzer)
  - [Power Level Analyzer: What It Does](#power-level-analyzer-what-it-does)
  - [Power Level Analyzer: Inputs](#power-level-analyzer-inputs)
  - [Power Level Analyzer: Quick Walkthrough](#power-level-analyzer-quick-walkthrough)
- [Damage Calc](#damage-calc)
  - [Damage Calc: What It Does](#damage-calc-what-it-does)
  - [Damage Calc: Inputs](#damage-calc-inputs)
  - [Damage Calc: Quick Walkthrough](#damage-calc-quick-walkthrough)
- [Guild Battle](#guild-battle)
  - [Guild Battle: What It Does](#guild-battle-what-it-does)
  - [Guild Battle: Inputs](#guild-battle-inputs)
  - [Guild Battle: Quick Walkthrough](#guild-battle-quick-walkthrough)
- [Guild Battle Test](#guild-battle-test)
  - [Guild Battle Test: What It Does](#guild-battle-test-what-it-does)
  - [Guild Battle Test: Inputs](#guild-battle-test-inputs)
  - [Guild Battle Test: Quick Walkthrough](#guild-battle-test-quick-walkthrough)
- [Zelarith Assignment](#zelarith-assignment)
  - [Zelarith Assignment: What It Does](#zelarith-assignment-what-it-does)
  - [Zelarith Assignment: Inputs](#zelarith-assignment-inputs)
  - [Zelarith Assignment: Quick Walkthrough](#zelarith-assignment-quick-walkthrough)
- [Should I Attack](#should-i-attack)
  - [Should I Attack: What It Does](#should-i-attack-what-it-does)
  - [Should I Attack: Inputs](#should-i-attack-inputs)
  - [Should I Attack: Quick Walkthrough](#should-i-attack-quick-walkthrough)
  - [Should I Attack: Quick Stats](#should-i-attack-quick-stats)
- [Support Team Builder](#support-team-builder)
  - [Support Team Builder: What It Does](#support-team-builder-what-it-does)
  - [Support Team Builder: Quick Start](#support-team-builder-quick-start)
  - [Support Team Builder: Inputs](#support-team-builder-inputs)
  - [Support Team Builder: Quick Walkthrough](#support-team-builder-quick-walkthrough)
- [Gear Search](#gear-search)
  - [Gear Search: What It Does](#gear-search-what-it-does)
  - [Gear Search: Inputs](#gear-search-inputs)
  - [Gear Search: Quick Walkthrough](#gear-search-quick-walkthrough)
- [Enemy Stats](#enemy-stats)
  - [Enemy Stats: What It Does](#enemy-stats-what-it-does)
  - [Enemy Stats: Inputs](#enemy-stats-inputs)
  - [Enemy Stats: Quick Walkthrough](#enemy-stats-quick-walkthrough)
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
- Should I Attack Bulk Diagnostics

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

Synergy note:
- `Torpor` is available in the synergy bonus override panel as a short-duration damage-vulnerability effect.

### Power Level Analyzer: Quick Walkthrough
Required steps:
1. Select a survey sheet or upload a CSV file.
2. Set `Enemy Weakness`, `Preferred Damage Type`, and `Enemy Count Weighting`.
3. Click `Find Best Teams`.

Expected output:
- Ranked player/team results sorted by score.
- Guild assignment output based on configured guild rules.
- Per-player context fields (for example submitted guild/banner response) shown in results.

---

## Damage Calc
### Damage Calc: What It Does
Calculates expected damage values for physical, magical, and mixed command abilities using workbook-aligned formulas, with optional summon/limit-break and advanced multiplier inputs.

### Damage Calc: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Damage Type | Physical / Magical / Physical/Magical | Controls which attack/defense fields are required |
| P.Atk / M.Atk | Whole number | Main attack stats for physical/magical paths |
| Enemy PDEF / Enemy MDEF | Whole number | Enemy defense stats for physical/magical paths |
| Weapon C.Ability Potency | Percent (whole or up to 2 decimals) | Core ability multiplier |
| Build/Enemy/Advanced percent fields | Percent (whole or up to 2 decimals) | Optional buffs, debuffs, and multipliers |
| Summon / Limit Break fields | Mixed selects + percentages | Optional summon/LB path tuning |

### Damage Calc: Quick Walkthrough
Required steps:
1. Set `Damage Type`.
2. Fill required core fields (`P.Atk`/`M.Atk` and `Enemy PDEF`/`Enemy MDEF` based on mode), plus `Weapon C.Ability Potency`.
3. Add optional build/enemy/advanced inputs as needed.
4. Click `Calculate`.

Expected output:
- Damage range, average damage, bonus damage, total damage, and average LB/summon damage.
- Workbook helper diagnostics in the expandable `Diagnostics` section.

Notes:
- Damage Calc saves your `Input_*` values in browser local storage (`damage-calc-state-v1`) and restores them on revisit.
- `Reset` clears both form values and saved local browser state for this page.

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

### Guild Battle: Quick Walkthrough
Required steps:
1. Select a guild battle sheet or upload a CSV file.
2. Set `Current Day` (if different from default).
3. Click the play button.

Expected output:
- `Today Summary` with interpreted current stage state.
- Player percentage/profile context used by the simulation.
- `Battle Plan for Today` with stage assignments, final HP projection, and attack log details.

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

### Guild Battle Test: Quick Walkthrough
Required steps:
1. Select a guild battle sheet or upload a CSV file.
2. Set `Current Day` and `Number of Runs`.
3. Click `Run Multi` (or `Run Single Detailed`).

Expected output:
- Aggregate simulation metrics (average resets, clear rates, final HP trends).
- Best/worst run comparison based on selected scoring mode.
- Assignment frequency distribution by player and stage.

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

### Zelarith Assignment: Quick Walkthrough
Required steps:
1. Ensure `Dispatcher:Path` points to a valid dispatcher directory.
2. Select a guild battle sheet or upload a CSV file, then set `Day`.
3. Click `Run Dispatcher`.

Expected output:
- Raw dispatcher console output.
- Parsed stage assignments extracted from dispatcher output.
- Re-simulated battle summary/aggregate metrics for the parsed plan.

---

## Should I Attack
### Should I Attack: What It Does
Runs multi-simulation recommendation analysis for one selected player and advises `Attack now` vs `Hold` using simulation evidence.

The page supports two paths:
- Standard mode (unchecked): chooses a run based on earliest selected-player attack timing relative to reset horizon.
- Immediate-use mode (checked): prioritizes runs where the selected player takes the first non-reset hit when possible, then chooses by clears/HP/points.

### Should I Attack: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Guild | One configured `GoogleSheets:GuildBattleSheets` option | Source battle data |
| Player | One parsed player from selected sheet | Target player for recommendation |
| Day | Day 1/2/3 (auto-suggested, user-overridable) | Builds simulation state for selected day |
| I need to use my attacks now | Checked/unchecked | Enables immediate-use recommendation path |

### Should I Attack: Quick Walkthrough
Required steps:
1. Select a guild and wait for the player list to load.
2. Select player and confirm/correct the day.
3. Optionally enable `I need to use my attacks now`.
4. Click `Analyze` and confirm in the modal prompt.

Expected output:
- Recommendation badge (`ATTACK NOW` or `HOLD`) with rationale.
- Simulated stage-hit summary for selected player (including split-stage cases).
- Best-run evidence snapshot (run source, resets, final HP, clears, points).
- Immediate recommendation source details (run-aligned stage vs heuristic fallback when needed).

### Should I Attack: Quick Stats
- Simulation runs per analysis: `25`
- Day range: `1-3`
- Standard mode best-run selection: earliest selected-player attack, then resets, then final HP sum
- Immediate-use best-run selection: strict first non-reset selected-player hit when available, then clears, then final HP sum, then points
- Recommendation warning includes simulation variance and leadership check guidance

### Should I Attack Bulk Diagnostics (Leadership)
- Leadership-only batch page that evaluates all parsed players for one selected guild sheet.
- Provides recommendation totals, fallback usage visibility, and per-player rationale output.

---

## Support Team Builder
### Support Team Builder: What It Does
Builds and ranks support-team weapon combinations from local UnknownX7 data (`external/UnknownX7/FF7EC-Data`) without requiring Google Sheets.

The page mirrors the original support-builder flow with effect selectors, range/potency thresholds, character constraints, and ranked team output.

Reactive beta page:
- `/SupportTeamBuilderVue` hosts a Vue-based client that uses the same backend matching/ranking logic with in-page updates instead of full postback refreshes.
- `/SupportTeamBuilder` remains available as the legacy Razor-postback implementation during phased migration.

### Support Team Builder: Quick Start
1. Open `/SupportTeamBuilder` and confirm your effect filters.
2. Click `Build Teams` once to generate initial rankings.
3. Tune `Owned OB` values in matching weapon cards and `Owned/Not Owned` in matching outfit cards; rankings auto-refresh after each change.

Quick notes:
- A beta notice is shown at the top of the page; behavior may continue to evolve.
- Use `Open original FF7EC Support Team Builder` for side-by-side comparison with the source experience.

### Support Team Builder: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Effect Filters | One or more selected effects | Required effects to satisfy |
| Range | All / Single+All / Self+Single+All | Range gate per filter |
| Min Base Potency | Low/Mid/High/ExtraHigh | Base potency floor |
| Min Max Potency | Low/Mid/High/ExtraHigh | Max potency floor |
| Max Characters | 1/2/3 | Character-count cap in team assignments |
| Must Have | Character checkboxes | Require selected characters in final teams |
| Exclude | Character checkboxes | Block selected characters from teams |
| Owned OB (per weapon) | Not Owned / OB0 / OB1-5 / OB6-9 / OB10 | Weapon availability and potency context |
| Owned Outfit (per outfit) | Owned / Not Owned | Outfit availability for support matching |

### Support Team Builder: Quick Walkthrough
1. Add one or more effect filters.
2. Choose range and potency thresholds for each filter.
3. Set maximum characters and optional must-have/exclude character constraints.
4. Click `Build Teams`.
5. (Optional) adjust weapon `Owned OB` and outfit `Owned/Not Owned` selectors; team rankings refresh automatically.
6. Use `View details` on a weapon to inspect ability text and customization details in the modal.

Vue beta notes:
- Ownership selector changes trigger in-page refreshes through API calls (no full page reload).
- `Build Teams` still performs an explicit full recalculation using your current filter + constraint state.

Notes:
- Owned weapon/outfit selections are saved in browser local storage (`support-team-builder-state-v1`).
- For effects that do not expose explicit potency tags (`[Pot]`/`[Max Pot]`) in source ability lines, potency dropdowns are auto-locked to `Low` and an inline note explains that potency thresholds are not applicable.
- Ranking follows the current support-builder precedence: max potency score, fewer characters, fewer weapons, then base potency score.
- Ranked team rows now show a character's selected outfit after weapon names when present.

---

## Gear Search
### Gear Search: What It Does
Provides searchable/filterable weapon/costume data with ability details, R abilities, sigils, compare mode, and level/OB snapshots.

### Gear Search: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Character checkboxes | One or more characters | Character filtering |
| Search | Free text | Name/ability/effect search |
| Rows per page | 25/50/100/200 | Card result paging |
| Advanced filter checkboxes | Element, ability, range, equipment, effects, R abilities, Sub-R abilities, sigils, etc. | Narrow results |
| Has Customizations | Checkbox | Show only entries with customization data |
| View Levels modal Overboost | 5★ (OB0) to OB10 | Snapshot at specific overboost |
| View Levels modal Level | 1-130 (slider/number) | Snapshot at specific weapon level |

### Gear Search: Quick Walkthrough
Required steps:
1. Enter a search term and/or select one or more filters.
2. Review matching results in the card list (mobile and desktop).
3. Use a card action (`Show Ability Details`, `View Levels`, or compare toggle) on a matching result.

Expected output:
- Filtered weapon results in card layout with key stats/effects.
- Card actions for `View Levels`, `Show Ability Details`, and compare selection.
- `R Abilities` shown inline on desktop cards, with tap/click affordances on mobile.
- Customization-added `R Abilities` mirror base `R Abilities` by showing `+points` and resolved effect details.
- Compare panel updates as items are added.

### Important UI Note
- Customizations unlock at `OB1+`.
- At `5★/OB0`, customizations are intentionally unavailable.
- View Levels modal `R Abilities` show `+points/name` on the first line and one-or-more resolved effect lines beneath.
- View Levels modal includes customization-added `R Abilities` in the same `R Abilities` list with the same points/effect presentation behavior.
- View Levels modal labels customization-added `R Abilities` with a `Cust.` badge to distinguish optional unlocks from base passives.
- Customization-added `R Abilities` are included in advanced `Sub-R Abilities` filtering via their resolved passive effect categories.
- `Sub-R Abilities` filters match semantic passive-effect categories (for example `HP` and `HP Gain` are distinct).

---

## Enemy Stats
### Enemy Stats: What It Does
Searches enemy and stage names and displays detailed boss metadata: stats, resistances, immunities, and text descriptions.

### Enemy Stats: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Boss or stage name | Free text (type 2+ characters for suggestions) | Finds enemy-name matches and stage-name matches |
| Search button | Click | Runs query |
| Show Details button | Per card | Opens detailed enemy modal |

### Enemy Stats: Quick Walkthrough
Required steps:
1. Enter a boss or stage name in the search box.
2. Click `Search`.
3. Click `Show Details` on a result card.

Expected output:
- Matching enemies/stages in responsive result cards (single-column on smaller screens, 3-column desktop grid).
- Detail modal with stats, resistances, and immunities for the selected result.
- Result behavior aligned with match type (`Enemy` match shows `N/A` stage; `Stage` match shows stage name).

### Result Behavior
- Enemy-name match: Stage shows `N/A`.
- Stage-name match: Stage column shows stage name; name/level show the matching enemy and level.

---

## Data Diagnostics
Leadership page used to verify gear enrichment status and detect missing weapon/costume enrichment data.

Includes a `Reload UnknownX7 Data` action for leadership users to trigger a full `WeaponSearchDataService` refresh without restarting the app.

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
