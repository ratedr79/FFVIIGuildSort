# SOLDIER Tools

A web toolkit for Final Fantasy VII Ever Crisis guild operations: team ranking, guild battle simulations, assignment testing, gear lookup, and enemy data lookup.

## Table of Contents
- [Getting Started](#getting-started)
- [Access and Leadership Tools](#access-and-leadership-tools)
- [Home Page](#home-page)
- [Player Power Analyzer V2](#player-power-analyzer-v2)
  - [Player Power Analyzer V2: What It Does](#player-power-analyzer-v2-what-it-does)
  - [Player Power Analyzer V2: Inputs](#player-power-analyzer-v2-inputs)
  - [Player Power Analyzer V2: Quick Walkthrough](#player-power-analyzer-v2-quick-walkthrough)
- [Player Inventory](#player-inventory)
  - [Player Inventory: What It Does](#player-inventory-what-it-does)
  - [Player Inventory: Import Options](#player-inventory-import-options)
  - [Player Inventory: Character Stats](#player-inventory-character-stats)
- [Power Level Analyzer](#power-level-analyzer)
  - [Power Level Analyzer: What It Does](#power-level-analyzer-what-it-does)
  - [Power Level Analyzer: Inputs](#power-level-analyzer-inputs)
  - [Power Level Analyzer: Quick Walkthrough](#power-level-analyzer-quick-walkthrough)
- [Damage Calc](#damage-calc)
  - [Damage Calc: What It Does](#damage-calc-what-it-does)
  - [Damage Calc: Inputs](#damage-calc-inputs)
  - [Damage Calc: Quick Walkthrough](#damage-calc-quick-walkthrough)
- [Guild Battle Sheet](#guild-battle-sheet)
  - [Guild Battle Sheet: What It Does](#guild-battle-sheet-what-it-does)
  - [Guild Battle Sheet: Inputs](#guild-battle-sheet-inputs)
  - [Guild Battle Sheet: Quick Walkthrough](#guild-battle-sheet-quick-walkthrough)
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
- [Interactive Team Builder](#interactive-team-builder)
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
The Home page lists all available tools with short descriptions and lock icons for restricted pages. The card order leads with the personal-loadout tools — Player Power Analyzer V2 (`NEW`), Interactive Team Builder (`NEW`), and Player Inventory (`Updated`) — followed by Gear Search, Guild Battle Sheet, Enemy Stats, and the remaining utility and leadership tools.

---

## Player Power Analyzer V2
### Player Power Analyzer V2: What It Does
Builds and scores a recommended 3-character team from your own **local inventory** (the owned weapons/costumes and levels you save in [Player Inventory](#player-inventory)), and is the primary single-player analysis tool. Unlike the survey-based [Power Level Analyzer](#power-level-analyzer), it does not need a Google Sheet — it reads the browser's `player-inventory-state-v1` state directly.

It models a full loadout explicitly: a main hand, off hand, ultimate weapon, main outfit, shared sub-weapons, and (optionally) boss immunities and required/preferred effects. Off-hand and ultimate weapons use their abilities as active utility; sub-weapons and sub-outfits contribute half-value stats and passive points only. The result is one recommended team plus alternates, each with a relative score, per-character PATK/MATK/HEAL, provided effects, passive totals, and an assumed materia setup.

> The page is marked **BETA** and under active development. If a recommendation looks off, you can export a debug "repro" snapshot (it captures your inventory and settings) and send it for investigation; instructions are at the bottom of the page.

### Player Power Analyzer V2: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Saved inventory | Your `player-inventory-state-v1` (from Player Inventory) | Only gear you own is considered; no sheet required |
| Enemy Weakness | None/Earth/Fire/Ice/Lightning/Water/Wind/Holy/Dark | Sets the elemental offensive axis |
| Preferred Damage Type | Any/Physical/Magical | Biases scoring toward physical or magical builds |
| Target Scenario | Single/Multiple enemy context | Influences single-target vs AoE priorities |
| Search Mode | `Fast · ~15s` / `Full · ~60s` | Search breadth vs speed (see below) |
| Pro: deeper sub-weapon optimization | Checkbox (Full mode only) | ~87s; picks sub-weapons by exact damage contribution |
| Team Templates | Enable one or more templates | Restricts valid team composition shapes |
| Boss Immunities (Advanced) | Checkboxes by group | Invalidates builds relying on an immune effect |
| Required & Preferred Effects (Advanced) | Off / ⭐ Preferred / 🔒 Required per effect | Required invalidates builds lacking the effect; Preferred boosts score |

Search modes:
- **Fast (~15s)** — searches a reduced set of your strongest options and skips clearly-behind candidates; good for rough ideas, may differ from Full.
- **Full (~60s, recommended)** — searches your full armory for the most accurate recommendation. Byte-identical to the reference baseline.
- **Pro (Full only, ~87s)** — adds exact-damage-contribution sub-weapon selection for the most refined build; most accurate, slowest.

### Player Power Analyzer V2: Quick Walkthrough
Required steps:
1. Build up your inventory first in [Player Inventory](#player-inventory).
2. Open `/PlayerPowerAnalyzerV2`, set `Enemy Weakness`, `Preferred Damage Type`, and `Target Scenario`.
3. Choose a `Search Mode` (and optionally `Pro`), and expand `Advanced filters` if you need boss-immunity or required/preferred-effect constraints.
4. Click `Analyze V2 Team`.

Full and Pro analyses can take roughly a minute or more, so they run in the background: the page shows an "Analyzing V2 Team" overlay, keeps the request sub-second, and updates automatically when the result is ready (keep the tab open). This also avoids long runs timing out behind the hosting proxy.

Expected output:
- A recommended team with a relative model score (a unitless ranking number, not literal in-game damage), plus selectable alternate teams with their Adds/Drops vs the recommended team.
- Per-character build-out: weapons (main/off/ultimate + sub-weapons), outfit + sub-outfits, PATK/MATK/HEAL, provided effects, top passive totals, and an assumed materia setup.
- Matched/missing hard-required and preferred effects, plus inventory-coverage notes (characters available, weapons with unset levels).
- A quick copy-text block for sharing, and an `Export V2 Repro File` action for feedback.

---

## Player Inventory
### Player Inventory: What It Does
Your local armory. Player Inventory (`/PlayerInventoryManagement`) tracks which weapons and costumes you own, at a simplified level/overboost, entirely in your browser — no account login. It feeds [Player Power Analyzer V2](#player-power-analyzer-v2) and the [Interactive Team Builder](#interactive-team-builder).

Inventory is saved to browser-local `player-inventory-state-v1`. Weapon ownership is `Do Not Own` / `3★` / `4★` / `5★` / `OB1`–`OB10` (ultimates use a simple owned flag); costumes are owned / not owned. You can export the inventory to a JSON file and import it back as a backup.

### Player Inventory: Import Options
| Option | Use |
|---|---|
| Import (JSON) | Load a previously exported inventory backup file. |
| Paste Import | Paste CSV/TSV or simple `Name - OB - Level` lines, with header auto-detection, a per-row preview, and merge/replace modes. |
| Import from Sheet | Paste three labeled boxes copied from the community Google tracking sheet's tabs (Weapons / Ultimate Weapons / Costumes). |
| SOLDIER Survey Quick View | Compare a configured survey sheet's gear columns against your saved inventory. |

**Import from Sheet** is the fastest way to seed your inventory from the community tracking sheet:
- Paste each tab into its matching box: Weapons (`Overboost · Level · Weapon`), Ultimate Weapons (`Owned · Lv · Weapon`, the `[N Uses]` suffix is ignored), and Costumes (`Owned · Gear`).
- Names resolve against the live catalog; a name that maps to more than one entry (a rerun/variant) is applied to all matches, never dropped.
- **Apply replaces your entire saved inventory** — anything not listed in the boxes becomes Do Not Own / Not Owned. A preview shows per-tab matched/owned/unmatched counts and warns if a box is left empty (that whole category would be wiped).

### Player Inventory: Character Stats
A **Character Stats** panel (with an "Enter Character Stats" modal) records per-character stats so analysis tools can use your **real attack stats** instead of weapons-only estimates. This is stored separately in browser-local `player-character-stats-v1` and never leaves your browser.

What you enter:
- An account-wide **Highwind Bonus** (%) for each of the six stats (HP, PATK, MATK, PDEF, MDEF, HEAL).
- Per character: a **Level**, then three stat blocks — **Base**, **Character Stream**, and **Role Stream** (each across the six stats).

How it fills in:
- **Base** auto-fills (read-only) from the selected character's Level, using game data.
- **Character Stream** and **Role Stream** default to their **max** (every growth-board node unlocked) and reproduce the in-game stream values. They stay editable — lower them if your trees aren't fully unlocked. Use **Fill streams from max** to re-apply the maxes, or **Reset to Defaults** to restore base + max.
- A live **Computed Total** per stat shows `floor((Base + Character Stream + Role Stream) × (1 + Highwind%))` — your stats *before* weapons and branding, matching the in-game "Base Stats" total within about 1. Values are approximate (the game floors, and weapon branding is RNG and not recorded).

The Highwind section also records three Highwind-only ability-potency lines:
- **Boost Wpn. C. Ability Pot.** — feeds the Interactive Team Builder's damage estimate as the weapon command-ability potency bonus.
- **Boost Mat. C. Ability Pot.** and **Boost Limit Ability Pot.** — recorded for reference only; materia and limit abilities are outside the team-builder damage estimate.

Once a character has stats entered, the [Interactive Team Builder](#interactive-team-builder) shows that character's true total PATK/MATK and an estimated per-cast damage.

---

## Power Level Analyzer
### Power Level Analyzer: What It Does
Builds and scores 3-character teams from guild survey data, ranks players, and assigns players to guilds using configured rules.

Current scoring highlights:
- DPS main-hand scoring still carries the largest direct-damage weight and prefers teased weakness + preferred damage type matches.
- DPS off-hand scoring now behaves more like a hybrid utility slot: direct potency is reduced unless the weapon also brings relevant synergy/coverage, and elemental mismatch can zero direct damage contribution.
- Team synergy bonus now comes from weighted weapon synergy categories with diminishing returns instead of raw additive match count.
- Outfit scoring gives the selected main outfit full value, while sub outfits contribute half passive value and no command ability value.

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

A full player-base analysis can take several minutes. It runs in the background with an "Analyzing the player base..." overlay showing elapsed time; keep the tab open and the page updates automatically when it finishes. (This also prevents long runs from timing out behind the hosting proxy.)

Expected output:
- Ranked player/team results sorted by score (descending). Under the `Use V2 Engine` option, players are ranked by their V2 score rather than the order they appeared in the input.
- Guild assignment output based on configured guild rules.
- Per-player context fields (for example submitted guild/banner response) shown in results.
- Click a player's in-game name in the ranked table to open a submitted-gear modal (weapons/costumes by character, utility items, materia summary, and missing-catalog hints).
- Debug view now exposes weighted team synergy notes, richer weapon sub-scores, and raw-vs-weighted outfit synergy details.
- An `Export guilds CSV` button appears after an analysis runs. It exports exactly what is on screen — it honors both the async result and the `Use V2 Engine` checkbox (reading the completed background job's result bundle) instead of re-running the analysis.

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

## Guild Battle Sheet
### Guild Battle Sheet: What It Does
Builds a month-selectable, featured-only recommendation sheet for the current guild battle element and damage type using the shared Gear Search weapon dataset.

It is designed for fast monthly reference, not battle-log simulation. The page automatically ranks featured weapons into headline recommendations, potency suggestions, sub-weapon candidates, and elemental utility sections.

The main featured recommendation area supports two ranking views:
- `Traditional`: ranks the featured pool directly and surfaces the strongest battle-fit DPS weapons plus standout fight utility.
- `Character`: returns at most one main recommendation per character, favoring true battle-fit DPS packages first and then support-style pivots when a character lacks a proper battle-fit main hand.

### Guild Battle Sheet: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Month dropdown | One configured month from `data/guildBattleSheets.json` | Switches between current and historical guild battle sheets |
| Recommendation Mode | `Traditional` / `Character` | Changes only the `Main Featured Recommendations` section |
| Debug mode | Checked/unchecked | Shows matching reasons used by the automatic ranking rules |

### Guild Battle Sheet: Quick Walkthrough
Required steps:
1. Open `/GuildBattleSheet`.
2. Leave the newest month selected or choose an older month from the dropdown.
3. Choose `Traditional` or `Character` mode for the main featured section.
4. Review `Main Featured Recommendations`, `Write-Up`, potency picks, sub-weapon picks, and the bottom utility sections.
5. (Optional) enable `Debug mode` to inspect the full internal match reasons.

Expected output:
- Latest configured month selected by default.
- A featured-only recommendation sheet for the selected `Element + Physical/Magical` battle type.
- Main recommendations biased toward matching element/type DPS weapons first, then toward party-facing or boss-facing utility that helps the current fight.
- Self-only utility on off-fit DPS weapons does not count as fight-facing support for the main featured section.
- Character mode can show package labels like `DPS Package`, `Support Package`, or `Support Pivot` to explain why a character is present.
- Main recommendation cards show a compact score pill and an `Included because:` summary, and may show relevant customization details when those customizations help explain the recommendation.
- An optional manual monthly write-up when present in config; otherwise a compact auto-generated summary.
- Clickable weapon art that opens the larger shared preview modal path already used by Gear Search.

Maintenance notes:
- Monthly definitions live in `data/guildBattleSheets.json`.
- The page only recommends weapons whose dataset `EquipmentType` resolves to `Featured`.
- `topPicks` can pin featured weapons to the top of the main list.
- `hiddenWeapons` can suppress edge-case results without code changes.
- Optional `conditionalMechanics` entries can be added to a month definition, but they only affect ranking when `Debug mode` is enabled.
- `Traditional` / `Character` mode changes only the main featured section; potency, sub-weapon, and lower utility sections stay the same.
- The lower utility sections are broader than the main featured section and can pull from the wider visible pool rather than only featured items.

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

## Interactive Team Builder
### Interactive Team Builder: What It Does
Lets you hand-pick a 3-character team from your own armory and see its estimated strength update live, scored by the same engine as Player Power Analyzer V2. Useful for "what if" planning and comparing specific loadouts rather than letting the analyzer choose for you.

### Interactive Team Builder: Inputs
- Your saved inventory (from Player Inventory Management) — only gear you own is selectable.
- For each of 3 characters: a character, main hand, off hand, ultimate weapon, main costume, 3 sub weapons, and 2 sub costumes.

### Interactive Team Builder: Quick Walkthrough
1. Pick a character in each cell, then use the **Choose Weapon** / **Choose Costume** buttons to open a picker. The picker shows each item's ability and passive R-abilities, and supports searching by ability text and filtering by element or specific R-abilities.
2. Build out the slots. Picking a weapon already used elsewhere clears it from the other slot (no duplicates); picking a character already in another cell moves it there.
3. The results panel updates with a relative team score (comparable to Analyzer V2 — a ranking number, not literal in-game damage), each character's PATK/MATK, an estimated per-cast damage, the active buffs/effects and debuffs, and per-character + team-wide R-ability levels (with a breakpoint-chart view).
4. Use the quick copy-text block to share the build.

#### True attack stats and estimated damage
When a character has **Character Stats** entered in [Player Inventory](#player-inventory), the results table shows that character's **true total attack stat**: `floor((base + character stream + role stream + weapons) × (1 + Highwind%) × passive multiplier)`, where the passive multiplier folds in the always-on Boost PATK / MATK / ATK R-abilities (self plus team-wide "All Allies"). Characters *without* Character Stats fall back to a weapons-only PATK/MATK marked with an asterisk (`*`).

The **Est. Dmg** column shows the estimated average damage of one cast against a standard reference enemy (enemy PDEF/MDEF 100, with a ×2.0 elemental weakness), fed by that real attack stat. The carry ability is chosen as the equipped damage weapon whose element matches the enemy weakness → else the highest-potency damage weapon → else a 300% fallback. The estimate includes the character's ability-potency passives and the team's detected buffs/debuffs (PATK/MATK Up, DEF Down, elemental res down, weapon boosts, amplification, etc.). It shows `—` when Character Stats aren't entered (there is no real attack stat to feed it).

Both numbers are **approximate** — weapon branding and exact in-battle rotation specifics aren't modeled, so the estimate sits slightly conservative. The team **Score** remains a relative ranking number (not literal in-game damage); the **Est. Dmg** column is the absolute-damage estimate.

---

## Gear Search
### Gear Search: What It Does
Provides searchable/filterable weapon/costume data with ability details, R abilities, sigils, compare mode, level/OB snapshots, clickable gear-art previews, quick combo filters for common elemental physical/magical searches, and emphasized result headers that make weapon titles and character identity easier to scan.

### Gear Search: Inputs
| Input | Expected Value | Use |
|---|---|---|
| Character checkboxes | One or more characters | Character filtering |
| Search | Free text | Name/ability/effect search |
| Rows per page | 25/50/100/200 | Card result paging |
| Buff/Debuff Notes | Button | Opens the in-page reference modal with ally/enemy tier and status notes |
| Quick Filters | Equipment shortcuts plus `Earth/Fire/Ice/Lightning/Water/Wind` paired with `Phys.` or `Mag.` | One-click combo filtering for common element + damage-type searches |
| Advanced filter checkboxes | Element, ability, range, equipment, effects, R abilities, Sub-R abilities, sigils, etc. | Narrow results |
| Has Customizations | Checkbox | Show only entries with customization data |
| View Levels modal Overboost | 5★ (OB0) to OB10 | Snapshot at specific overboost |
| View Levels modal Level | 1-140 (or lower effective level if a weapon clamps to its supported max) | Snapshot at specific weapon level |

### Gear Search: Quick Walkthrough
Required steps:
1. Enter a search term and/or select one or more filters.
2. Review matching results in the card list (mobile and desktop).
3. Use a card action (`Show Ability Details`, `View Levels`, or compare toggle) on a matching result.

Expected output:
- Filtered weapon results in card layout with key stats/effects.
- Card actions for `View Levels`, `Show Ability Details`, and compare selection.
- A `Buff/Debuff Notes` button near the page title that opens a quick-reference modal for ally/enemy buff tiers and special status notes.
- Quick filter pills for common `Element + Phys./Mag.` combinations that can be stacked without broadening into the wrong element/type cross-product.
- Quick filter pills also include `Circle`, `Triangle`, `X`, and `Diamond` sigil shortcuts that stay synced with the advanced sigil checkbox filter.
- Element rows now show the matching elemental, non-elemental, or heal icon beside the element text in the same compact inline style used for stat icons.
- The compare modal now preserves the rendered element and stat stack presentation as well, instead of dropping the element value or flattening the stat pill layout when items are copied out of the results table.
- Sigil rows now use matching sigil icons from `wwwroot/images` in the same compact capsule style, with materia support levels shown as `I` / `II` instead of `x1` / `x2`.
- Result headers now use a dual-identity treatment: character accents stay structural via the portrait shell, border, and header tint, while element accents react through the weapon underline, element pill tinting, and subtle energy flare so the weapon role reads faster without overwhelming the card.
- The metadata pills under each weapon name now use a more consistent size and alignment across equipment, element, and type labels.
- Weapon and costume result headers now also render a manifest-backed item-art thumbnail beside the character portrait, falling back to `ui_icon_weapon.png` or `ui_icon_outfit.png` until a specific entry is backfilled.
- Clicking a weapon or outfit thumbnail now opens a larger in-page preview modal that prefers `/images/weapons/lg` or `/images/outfits/lg` art when available and otherwise falls back to the standard resolved image.
- The `View Levels` snapshot modal reuses the same streamlined portrait-led header treatment, including the iconized metadata pills and element-reactive accents, so the selected weapon context stays visually consistent after opening the detail view.
- The compare modal and `View Levels` snapshot header reuse the same resolved item-art URL, and the preview modal reuses the large-image lookup path, so backfilled gear art automatically appears across all Gear Search surfaces without additional UI work.
- `R Abilities` shown inline on desktop cards, with tap/click affordances on mobile.
- Customization-added `R Abilities` mirror base `R Abilities` by showing `+points` and resolved effect details.
- Compare panel updates as items are added.

### Important UI Note
- Customizations unlock at `Lv80+`.
- At levels below `80`, customization effects remain locked even at `5★/OB0`.
- Gear Search ability text resolves localized status names for costume/weapon effects and customizations instead of leaking raw internal IDs like `Status 31` or `Status Change 47`.
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

Also includes local image coverage checks for both standard and large gear art folders: `/images/weapons`, `/images/outfits`, `/images/weapons/lg`, and `/images/outfits/lg`.

---

## Configuration Needed
The app is driven by `appsettings.json` and data files:

- `GoogleSheets:SurveySheets` for Power Analyzer sheet options.
- `GoogleSheets:GuildBattleSheets` for battle simulation sheet options.
- `SharedAccess` for protected-page unlock behavior.
- `Dispatcher:Path` for Zelarith workflow.
- `data/stagePointCalibration.json` for stage point estimation.
- `data/guildBattleSheets.json` for monthly featured-only guild battle sheet definitions.
- `data/teamTemplates.json`, `data/guildRules.json`, `data/nameCorrections.json` for scoring and assignment controls.

---

## Important Accuracy Notes
- All analyzers depend on spreadsheet data quality.
- Missing/late/incorrect entries can produce inaccurate recommendations.
- Day selection and HP overrides strongly affect guild battle simulation output.
- Always sanity-check results against current in-game state before acting.

For developer-focused internals and maintenance details, see `docs/README.md`.
For recent documentation and behavior updates, see `docs/changelog.md`.
