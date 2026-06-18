# Changelog

All notable changes to this project should be documented in this file.

## Format
- Keep entries under `Unreleased` until deployment.
- Move completed items into a dated release section like `## 2026-04-02`.
- Use short bullets under these headings when possible:
  - `Added`
  - `Changed`
  - `Fixed`
  - `Docs`

---

## 2026-06-18

### Added
- Interactive Team Builder can now **Save / Load / Delete team builds** (client-side, localStorage `itb-saved-teams-v1`). Save (top action row) opens a name modal; an existing name shows an overwrite warning and the button switches to "Overwrite". Load opens a modal listing saved builds (name + character summary + saved date) that restores the team + battle context and re-scores; loading while the current build has content prompts for confirmation. Each row has a delete button that opens a confirmation modal. A saved record holds `{ id, name, team, battle, savedAt }` — gear/battle selections only (inventory + Character Stats apply fresh on load). Cross-tab `storage` events refresh the list; Escape closes the modals top-down. No server/engine changes.
- Player Power Analyzer V2 **Required Characters**: pick 0–3 characters (UI card above Team Templates) that every built team must include. Filtered during skeleton enumeration in `BuildTeamSkeletons` — the combo membership filter runs *before* the expansion-limit prune (so a valid required combo is never pruned), required teammates are force-included into the per-anchor support pool (so a low-ranked required character isn't cut by the `.Take`), and when all 3 slots are pinned, anchors outside the required set are skipped (search-narrowing speedup on large armories). Up-front validation (in `Analyze`) returns a clear `FailureReason` for >3 required, a required character with no owned main weapon, or a mutually-exclusive required pair (reuses `IsCharacterCombinationAllowed`, the same Sephiroth / Sephiroth (Original) rule templates use). Default empty → byte-identical to prior behavior (repro unchanged). `RequiredCharacters` added to `PlayerPowerAnalyzerV2Request`; page model binds it and exposes the roster via `AvailableCharacters`.

### Docs
- Added a Save/Load builds step to the README Interactive Team Builder walkthrough and noted the behavior in `docs/features/other-pages.md`.
- Added **Required Characters** to the README Player Power Analyzer V2 inputs table + walkthrough, and documented the engine behavior (skeleton filter / force-include / anchor short-circuit / validation) in `docs/features/player-power-analyzer-v2-max-damage.md`.

---

## 2026-06-17

### Added
- Interactive Team Builder results table now has an **Est. Dmg** column: the estimated average damage of one cast against a standard reference enemy (PDEF/MDEF 100, ×2.0 elemental weakness) when the character has Character Stats entered. `EstimateAverageDamage(...)` feeds the real `AttackPatk`/`AttackMatk` into the literal `DamageCalcService` model. Carry ability = the equipped damage weapon matching the enemy weakness element → else highest `DamagePercent` → else a 300% fallback. Includes the character's ability-potency passives (`SumAbilityPotencyPercents`), the account-wide `Boost Wpn. C. Ability Pot.` Highwind line (`HighwindWeaponPotencyBonus`), and the team's detected buffs/debuffs (`ApplyTeamEffectsToRequest`). Renders `—` when Character Stats aren't entered. Approximate (branding/rotation not modeled → slightly conservative); the team Score remains a relative model index. (Additive; the byte-identical analyzer repro is unchanged.)
- Player Inventory **Character Stats** Highwind section now records three Highwind-only ability-potency lines: `Boost Wpn. C. Ability Pot.` (consumed by the Interactive Team Builder damage estimate as the weapon command-ability potency bonus), `Boost Mat. C. Ability Pot.`, and `Boost Limit Ability Pot.` (recorded for reference only; materia/limit abilities are outside the team-builder damage estimate).

### Fixed
- Scoring engine now resolves the `Boost Ability Pot. (All Allies)` passive with its own breakpoint table (caps at Lv.3 = +15%) instead of falling through to the self `Boost Ability Pot.` table, fixing an over-credited team-wide ability-potency value.

### Docs
- Rewrote the end-user `README.md` to add a **Player Power Analyzer V2** section (local-inventory engine, Fast/Full/Pro search modes, advanced boss-immunity / required-preferred-effect inputs, async background run) and a **Player Inventory** section (ownership state, Import options incl. Import from Sheet, and the Character Stats panel incl. Highwind ability-potency lines). Updated the Table of Contents and the Home Page tool-order/badges description.
- Extended the README **Interactive Team Builder** section with true-attack-stat and **Est. Dmg** column behavior, and extended the **Power Level Analyzer** Expected-output with the score-ordered ranking and the async-+-V2-aware `Export guilds CSV`.
- Updated `docs/features/other-pages.md` Interactive Team Builder and Player Inventory entries with the Est. Dmg estimate (`EstimateAverageDamage`, reference enemy, carry selection, Highwind weapon-potency line) and reconciled the Character Stats entry to reflect Phase 2 being wired in.

---

## Unreleased

### Added
- Interactive Team Builder now shows each character's **true total attack stat** when Character Stats are entered: `ScoreFixedTeam` returns `AttackPatk`/`AttackMatk` = `floor((base + character/role stream + weapon) × (1 + Highwind%) × passive multiplier)`. Weapon uses the per-slot weighting already in `TotalPatk`/`TotalMatk` (main+ult full, off+sub half); the passive multiplier folds the always-on Boost PATK/MATK/ATK R-abilities (self + team-wide All-Allies; additive within a family, multiplicative across; Boost ATK affects both). Validated against an in-game Vincent loadout (sits a little under in-game = unlogged weapon branding). Characters without stats show a weapons-only value marked `*`. (Additive; team Score and the byte-identical analyzer repro unchanged.)
- Added **Character Stats** entry to `/PlayerInventoryManagement`: a panel + modal to record the account-wide Highwind bonus (six % boosts) and per-character stats (HP/PATK/MATK/PDEF/MDEF/HEAL), saved to browser-local `player-character-stats-v1`. The **Base** row auto-fills (read-only) from the character's **level**, and **Character Stream** / **Role Stream** auto-default to their max (every growth-board node unlocked) — all via a new `CharacterBaseStatsService` (loads `CharacterLevel.json` + `GrowthBoard*` data) + `?handler=CharacterStatDefaults`; streams stay editable (with "Fill streams from max" / "Reset to Defaults"). The live computed total `floor((Base + Character Stream + Role Stream) × (1 + Highwind%))` floors like the game and reproduces the in-game "Base Stats" total exactly (guarded by `CharacterBaseStatsTests`: base-floors + stream-maxes). Phase 2 will wire it into the Interactive Team Builder to feed real attack stats into the damage calc.
- Added a new public `/InteractiveTeamBuilder` page (Razor shell + Vue 3) to hand-build a 3-character team from owned gear and score it live with the same V2 engine (`ScoreFixedTeam`). Includes owned-only modal weapon/costume pickers (ability + half/full passive R-abilities, search, element + R-ability filters, in-use flags, All-Allies and customization passives), an inventory-aware catalog handler, and a results panel with relative score, per-character PATK/MATK, readable buffs/debuffs, per-character + team-wide R-ability levels with a breakpoint-chart modal, and a quick copy-text block.
- Added an **Import from Sheet** flow on `/PlayerInventoryManagement`: three labeled paste boxes (Weapons / Ultimate Weapons / Costumes) for the community Google tracking sheet's tabs. Reuses the structured paste parser (header detection now recognizes a `Gear` column; the ultimate `[N Uses]` suffix is stripped), resolves names against the live catalog (applying multi-variant names to all matches), and on apply **replaces the entire saved inventory** with a per-tab matched/owned/unmatched preview and empty-box warnings. Guarded by `SheetImportNameMatchingTests`.
- Added an in-process async analysis job system (`Services/AnalysisJobService` + `Services/AnalysisJobWorker`, registered in `Program.cs`) so long analyses run off the HTTP request thread. Player Power Analyzer V2 (Full/Pro) and the whole-player-base Power Level Analyzer now start a background job, poll a fast status endpoint behind an elapsed-time overlay, and redirect to a precomputed `?resultJobId=` result. This keeps every request sub-second and avoids Cloudflare's 100s origin-response timeout (error 524) on long runs. Synchronous handlers remain as no-JS fallbacks; engine behavior and the byte-identical repro signature are unchanged.
- Added a new public `/GuildBattleSheet` page that generates featured-only monthly guild battle recommendations from `WeaponSearchDataService`, including month selection, auto-generated write-up fallback, debug reasoning, and shared large-art preview modal support.
- Guild Battle Sheet main recommendations now expose a `Traditional` vs `Character` mode selector, package badges for character-mode cards, and always-visible explanation UI including score pills, `Included because:` summaries, and relevant customization details when needed.
- Added `docs/notes/buff-debuff-reference.md` as a user-readable reference for observed ally/enemy buff-debuff tier scaling and special status notes.
- Developer documentation set under `docs/`:
  - `docs/getting-started/onboarding.md`
  - `docs/architecture/application-overview.md`
  - `docs/configuration/configuration-reference.md`
  - `docs/features/power-analyzer.md`
  - `docs/features/guild-battle-simulation.md`
  - `docs/features/other-pages.md`
  - `docs/notes/special-cases-and-maintenance.md`
  - `docs/README.md`
- Added `docs/notes/sigil-effect-mapping-inference.md` to document the confirmed vs inferred Ultima Weapon sigil-resistance ID mappings.
- Initial changelog scaffold (`docs/changelog.md`).
- Gear Search now resolves passive-skill (`R Ability`) effects from FF7EC passive tables using OB/level point totals, including support for named passives that map to multiple effects.
- Gear Search advanced filters now include a `Sub-R Abilities` panel (normalized passive-effect categories) with in-panel search, clear, and `No matches found.` feedback.
- Added a new public `Should I Attack?` page to analyze a selected player from configured guild sheets and recommend `Attack now` vs `Hold` with simulation evidence.
- Added a leadership-only `Should I Attack Bulk Diagnostics` page for single-sheet batch recommendation runs and aggregate alerting.
- Added a new `FFVIIEverCrisisAnalyzer.Tests` xUnit project with orchestration-focused tests for `Should I Attack` tie-break, horizon, queue-priority, fallback, and immediate-mode behavior.
- Added a new public `Support Team Builder` page backed by local UnknownX7 JSON data (`external/UnknownX7/FF7EC-Data`) with multi-effect filters, range/potency thresholds, max-character constraints, must-have/exclude character filters, and ranked team output.
- Added `SupportTeamBuilderService` for effect matching, team assignment generation, duplicate filtering, and rank ordering aligned with support-builder precedence.
- Added browser-local persistence (`support-team-builder-state-v1`) for per-weapon owned OB selections used by the support-team workflow.
- Added leadership-triggered full UnknownX7 data reload support in `Data Diagnostics` via `WeaponSearchDataService.ReloadData()`.
- Added integration-style tests for support-team-builder option loading and max-character assignment constraints.

### Fixed
- Guild assignment now ranks players by team score inside `GuildAssigner` instead of trusting the order results arrived in. With the V2 engine this was wrong: the V2 adapter returns results in CSV/account order (the legacy engine pre-sorts by score), so guilds were filled in account order and strong players were scattered across guilds (e.g. Guild 2 starting higher than the bottom of Guild 1). Added `GuildAssignerTests` covering score-order fill and locked-player override.
- Power Level Analyzer "Export guilds CSV" now exports the currently displayed analysis (via `OnGetExportGuilds(resultJobId)` reading the completed job's result bundle) instead of re-running. This fixes two issues: the export previously always used the legacy engine regardless of the `Use V2 Engine` checkbox (so the CSV could disagree with the on-screen rankings), and it re-ran the full analysis synchronously (Cloudflare 524 risk). The export button is now a GET download that appears only after an analysis has been run.
- Fixed the Power Level Analyzer async start request using the wrong handler name (`?handler=StartAsync`); Razor strips the `Async` suffix, so it now calls `?handler=Start`. Both analyzer pages' status/result fetches now parse responses defensively (clear message instead of a raw "Unexpected token '<'" if a non-JSON response is ever returned).
- Excluded the non-player-obtainable duplicate "Buster Sword Origin" (Zack) record (id `20002`) from the catalog via `WeaponSearchDataService.ExcludedWeaponIds`, leaving only the obtainable id `20033`. The duplicate name previously made the inventory sheet importer treat the weapon as ambiguous; the master data exposes no obtainability flag, so the exclusion is hand-verified. Repro signature stays byte-identical.
- Player Power Analyzer V2 / Interactive Team Builder buff-debuff effects now classify polarity by effect family (a Weapon Boost is always an ally buff) and read each effect's own range marker, fixing a case (e.g. Chrome Death Penalty) where an ally Weapon Boost leaked the damage clause's `All Enemies` range and was mislabeled a debuff; the effect lists are also deduped so an effect renders once.
- Updated `WeaponSearchDataService` sigil-resistance backfills to use the confirmed/inferred Circle/Triangle/X/Diamond mappings instead of the earlier physical/magic resistance assumptions.
- Reclassified the previously reserved sigil-resistance family slots (`BuffDebuffType` `47` and `52`) as `Square Sigil Resistance Up/Down` based on newly identified square-sigil game asset evidence.
- Gear Search compare modal now preserves the rendered element display and full stat-stack markup when scraping selected rows, fixing a regression where the compare `Element` row could appear blank and keeping the compare stat row visually aligned with the main results.
- Gear Search ability/customization text now resolves additional status-change labels instead of leaking raw IDs for affected items such as `Elegant Gloves`, `Elegant Dress`, and `Crimson Blitz` customizations; stale amp-healing and ATB-conservation formatter cases in `WeaponSearchDataService` were corrected at the same time.
- Guild Battle Sheet main recommendation scoring now excludes self-only exploit-weakness and self-only element amplification effects from being treated as party/boss fight utility on off-fit DPS weapons, preventing Transgressor-style false positives.

### Changed
- Added `data/guildBattleSheets.json` as a runtime-copied monthly config source for the new Guild Battle Sheet page, with newest-month defaulting and override hooks for `topPicks` / `hiddenWeapons`.
- Guild Battle Sheet scoring now more explicitly separates party-facing support, boss debuff setup, and self-only DPS prep, adds target-breadth preference for broader support/setup coverage, and prefers fight-facing companions over self-only stat-stick companions in Character mode.
- Gear Search now includes a top-level `Buff/Debuff Notes` button that opens an in-page modal with ally-vs-enemy tier tables and special status notes for quick lookup while browsing gear.
- Power Analyzer scoring now fully reflects the latest overhaul: weighted synergy categories with diminishing returns, utility-biased DPS off-hand handling, weighted outfit synergy scoring, and debug output that explains the final selected-weapon synergy bonus.
- Gear Search quick filters now include dedicated `Earth/Fire/Ice/Lightning/Water/Wind` + `Phys.` / `Mag.` combo buttons for faster element-specific physical/magical browsing.
- Gear Search quick filters now also include `Circle`, `Triangle`, `X`, and `Diamond` sigil pills, wired as shortcuts over the existing advanced sigil checkbox filter state.
- Gear Search element rows now render matching elemental/non-elemental icons, plus the shared heal icon for `Heal`, alongside the text label using the same compact inline styling as the stat icon row on desktop and mobile cards.
- Gear Search sigil rows and legend now render with the new `ui_icon_sigil_*` assets in matching compact capsules, and materia support levels now display as `I` / `II` instead of `x1` / `x2`.
- Gear Search result and snapshot headers now use streamlined portrait-led panels with cropped character portraits, per-character accent styling, stronger weapon-name emphasis, and cleaner metadata pills across desktop, mobile, and `View Levels` modal layouts.
- Gear Search metadata pills under weapon titles now use normalized height, padding, and alignment so equipment, element, and type pills appear more visually consistent.
- Gear Search snapshot headers now use the same iconized metadata pill treatment as the desktop and mobile result headers for element/heal values.
- Gear Search now layers an element-reactive accent system on top of the existing character identity styling, using per-element color variables for weapon underlines, element pill tinting, subtle corner flares, and restrained hover energy without overpowering the character accents.
- Gear Search now resolves weapon/outfit art through a manifest-backed `GearImageCatalog` (`data/gearImages.json`) and falls back to `ui_icon_weapon.png` / `ui_icon_outfit.png` when no backfilled image is available.
- Gear Search desktop banners, mobile cards, compare headers, and `View Levels` snapshot headers now all render the shared resolved gear-art thumbnail beside the existing character portrait treatment.
- Gear Search gear-art thumbnails now open a shared preview modal that prefers large variants from `images/weapons/lg` and `images/outfits/lg`, with graceful fallback to the standard resolved image when a large asset is missing.
- Data Diagnostics now reports missing large gear art separately from the standard image checks for `images/weapons/lg` and `images/outfits/lg`.
- Damage Calc UX now persists calculator input state in browser local storage (`damage-calc-state-v1`), restores on revisit, and clears persisted state on `Reset`.
- Damage Calc percentage-oriented inputs now accept up to two decimal places while integer combat stat fields remain whole-number style.
- Damage Calc result layout now shows `Average LB/Summon Damage` in the result pane (instead of the summon input section).
- End-user `README.md` rewritten with:
  - table of contents
  - per-tool walkthroughs
  - UI input reference tables
  - configuration and accuracy notes
- Gear Search now uses Lv140 as the default/fallback weapon level for release readiness, while View Levels continues to reflect a lower effective level when a specific weapon clamps to its supported max.
- Gear Search advanced filters now include in-panel search + clear controls for long `R Abilities` and `Effects` lists, with `No matches found.` feedback while preserving selected checkbox state.
- Gear Search table `R Abilities` now expose hover details showing resolved OB10/max-level passive effects.
- Gear Search `R Ability` effect details now use tap/click-friendly popovers on mobile/touch devices (with outside-tap dismiss), while preserving hover/focus behavior on desktop.
- Gear Search View Levels modal now renders `R Abilities` in two-line format (`+points/name` plus one-or-more resolved effect lines) that updates as OB/level changes.
- Passive `R Ability` effect percentage rendering now supports `PassiveSkillType`-specific coefficient scaling (including `Type 8` resist-style values), fixing over-scaled percentage output.
- Gear Search results now render as cards across mobile and desktop (desktop uses a multi-column card layout), while preserving existing filter/search/compare interactions.
- Gear Search card behaviors were refined for readability and interaction consistency: desktop shows inline `R Ability` effect detail while mobile keeps tap/click-friendly affordances.
- Gear Search customization-added `R Abilities` now display `+points`, include resolved effect text in card/compare views, and participate in advanced `Sub-R Abilities` filtering.
- Gear Search View Levels modal now surfaces customization-added `R Abilities` inside the `R Abilities` section using the same point/effect rendering pattern as base `R Abilities`.
- Gear Search View Levels modal now adds a `Cust.` badge to customization-added `R Abilities` so optional unlock passives are clearly identified.
- Enemy Stats results now render as cards (single-column on smaller screens, 3-column desktop), with `Show Details` opening a modal instead of expanding inline.
- Enemy Stats search suggestions now use a custom mobile-friendly typeahead dropdown with outside-click dismiss and `Escape` close behavior.
- Guild battle dispatcher execution is now wrapped in a synchronized runner service so concurrent requests do not corrupt shared `input.txt`, and `Should I Attack` paths automatically fall back to engine-based simulation when dispatcher execution fails.
- `Should I Attack` now runs `25` simulations per analysis, exposes dispatcher/fallback run source in recommendation evidence, and applies mode-specific run selection:
  - standard mode: earliest selected-player attack, then resets, then final HP sum
  - immediate mode: strict first non-reset selected-player hit when available, then clears, final HP sum, and points
- Immediate-use recommendations now prefer a run-aligned stage (selected run's first selected-player attack stage) and expose heuristic fallback stage/source when run-aligned stage is unavailable.
- `WeaponSearchDataService` now tracks reload metadata (`LastLoadedUtc`, `ReloadCount`) and supports synchronized in-process full data reload.
- Support Team Builder matching-weapon `Owned OB` changes now auto-refresh ranked team results by auto-submitting search on selector change.
- Support Team Builder now includes a top-level external-link action to the original reference builder and a visible beta-status notice.
- Support Team Builder ranked team rows now allow opening weapon details modal from both main-hand and off-hand weapon names.

### Docs
- Documented the `/InteractiveTeamBuilder` and `/PlayerInventoryManagement` pages (incl. the Import from Sheet flow) in `docs/features/other-pages.md`, added an Interactive Team Builder section to the end-user `README.md`, and recorded the non-obtainable duplicate-weapon exclusion (Buster Sword Origin) under Data/Catalog Maintenance in `docs/notes/special-cases-and-maintenance.md`.
- Documented the async background analysis job system: new "Async Background Analysis Jobs" section in `docs/architecture/application-overview.md`, an "Async Analysis Job Notes" block (operational caveats + known follow-ups, incl. the still-synchronous CSV export) in `docs/notes/special-cases-and-maintenance.md`, a "Request Execution (Async Jobs)" section in `docs/features/power-analyzer.md`, and a pointer in `docs/features/player-power-analyzer-v2-max-damage.md`.
- Updated `README.md`, `docs/features/other-pages.md`, and `docs/configuration/configuration-reference.md` to document the new public Guild Battle Sheet page, its featured-only generation rules, and the `data/guildBattleSheets.json` maintenance format.
- Updated Guild Battle Sheet documentation in `README.md`, `docs/features/other-pages.md`, and `docs/configuration/configuration-reference.md` to cover recommendation modes, explanation UI, `conditionalMechanics`, current featured-vs-lower-section behavior, and recent self-only utility scoring refinements.
- Updated `README.md`, `docs/README.md`, and `docs/features/other-pages.md` to document the new Gear Search Buff/Debuff Notes modal and the supporting buff/debuff reference note.
- Updated Power Analyzer docs (`README.md`, `docs/features/power-analyzer.md`, `docs/notes/special-cases-and-maintenance.md`) to describe the current scoring model, including weighted team synergy, DPS off-hand utility weighting, outfit slot rules, and the latest debug breakdown output.
- Updated the user-facing Gear Search README section and developer docs to note that mapped status-condition/status-change effects now render localized names instead of raw internal IDs in ability/customization text.
- Updated the Gear Search and Data Diagnostics documentation to cover large-image preview modals plus separate `lg`-folder image coverage checks.
- Data Diagnostics reload flow now performs post-reload catalog re-enrichment (`WeaponCatalog.RefreshFromGearSearch()`) after `WeaponSearchDataService.ReloadData()`.
- Support Team Builder now includes outfit-aware matching and ranking: per-filter matching outfit results, owned/not-owned outfit state, one-outfit-per-character assignment, outfit-inclusive potency scoring, and duplicate filtering by combined weapon/outfit composition.
- Support Team Builder ranked-team rows now render selected outfit names after weapons when present.
- Support Team Builder weapon-details modal now includes customization details beneath ability text.
- Support Team Builder effect-filter potency controls now clearly handle non-potency effects: rows with effects lacking explicit `[Pot]`/`[Max Pot]` metadata auto-lock potency filters to `Low` and show inline guidance.
- Added a new hybrid Vue beta page at `/SupportTeamBuilderVue` with reactive in-page updates while preserving server-side matching/ranking logic via JSON handlers backed by `SupportTeamBuilderService`.
- Legacy `/SupportTeamBuilder` now includes a quick-link button to open the Vue beta page during phased migration.
- Power Analyzer ranked results now include a player-name gear modal showing submitted ownership details (weapons/costumes by character, utility items, materia summary, and missing-catalog hints).

### Fixed
- Weapon customization unlock behavior now follows the actual level gate of `Lv80+`, so `5★/OB0` weapons can expose customizations once they reach level 80.
- Gear Search snapshot modal now shows the customization unlock hint while the selected level is below `80`, and clears it once the weapon reaches the unlock threshold.
- Power Analyzer player gear modal stability issues causing repeated flashing/open-close loops on pointer movement were fixed by moving modals under `document.body`, avoiding invalid table-hosted modal markup, and guarding modal open/close handling.

### Docs
- Added end-user `README.md` documentation for `Damage Calc` with input expectations, conditional required-field behavior summary, and local persistence notes.
- Updated `docs/features/other-pages.md` with developer-facing `Damage Calc` architecture/behavior details (validation flow, persistence, percent input formatting, and formula version badge intent).
- Updated Power Analyzer docs (`README.md`, `docs/features/power-analyzer.md`) to document `Torpor` synergy support and override-key availability.
- Captured advanced logic and maintenance notes for:
  - Power Analyzer scoring and guild assignment behavior
  - Guild Battle simulation, aggregation, and dispatcher integration
  - configuration-driven behavior and operational caveats
- Added a direct deep-dive section for Power Analyzer synergy processing and effect weighting in `docs/features/power-analyzer.md#synergy-logic-and-effect-processing`.
- Added `Data Diagnostics` visibility for distinct `PassiveSkillType` values with representative sample outputs to help validate passive effect scaling behavior.
- Updated end-user `README.md` walkthrough/input sections for Gear Search and Enemy Stats to reflect card-based results, modal details flow, and Enemy Stats typeahead usage.
- Updated `docs/features/other-pages.md` entries for Gear Search and Enemy Stats to reflect current UI behavior.
- Added end-user `README.md` documentation for `Should I Attack` and leadership `Should I Attack Bulk Diagnostics`, including a quick stats section for simulation/run-selection behavior.
- Updated `docs/features/other-pages.md` with developer-facing details for `Should I Attack` orchestration, immediate-mode strict first-hit behavior, run-aligned stage recommendation, and bulk diagnostics intent.
- Added end-user `README.md` documentation for `Support Team Builder` inputs and walkthrough, plus `Data Diagnostics` reload behavior.
- Updated `docs/features/other-pages.md` with `Support Team Builder` architecture/data-flow details and leadership reload diagnostics behavior.
- Updated `README.md` Support Team Builder section with a user-facing quick-start and current UI behavior notes (beta banner, original-tool link, automatic ranking refresh on Owned OB changes).
- Updated `docs/features/other-pages.md` with technical behavior details for Support Team Builder modal entry points, auto-refresh interactions, and Data Diagnostics post-reload re-enrichment.
- Updated `README.md` Support Team Builder walkthrough/inputs to document outfit ownership controls, ranked outfit display, and weapon modal customization details.
- Updated `docs/features/other-pages.md` Support Team Builder internals to cover outfit assignment/scoring/dedupe logic and customization display in weapon details modal.
- Updated `README.md` and `docs/features/other-pages.md` Support Team Builder sections to document potency-filter applicability behavior and inline non-potency guidance.
- Added `README.md` and `docs/features/other-pages.md` documentation for the new `/SupportTeamBuilderVue` reactive beta flow and its shared backend parity model.
- Updated `docs/features/power-analyzer.md` with a `Player Gear Modal` section documenting data source expectations and UI stability guardrails to prevent modal flicker regressions.
