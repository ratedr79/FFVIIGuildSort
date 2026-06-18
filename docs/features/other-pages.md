# Other Pages and Tools

## Damage Calc (`/DamageCalc`)
- Purpose: workbook-parity damage calculator for physical/magical/mixed command paths with optional summon/LB and advanced multiplier tuning.
- Entry points:
  - UI page: `Pages/DamageCalc.cshtml`
  - Page model: `Pages/DamageCalc.cshtml.cs`
  - Core formulas: `Services/DamageCalcService.cs`
  - Request/result contracts: `Models/DamageCalcModels.cs`
- UI/validation behavior:
  - accordion sections for progressive disclosure across core/build/enemy/summon/advanced inputs
  - dynamic required-field rules driven by `Damage Type` (mode-based requirement toggling for `P.Atk`, `M.Atk`, `Enemy PDEF`, `Enemy MDEF`)
  - first-invalid-field submit behavior expands the relevant accordion section and focuses/scrolls to the invalid control
- Input formatting behavior:
  - integer stat fields remain whole-number style (`P.Atk`, `M.Atk`, `Enemy PDEF`, `Enemy MDEF`)
  - percentage fields accept whole numbers or up to 2 decimal places (client `step=0.01`)
  - percent inputs are normalized in service logic before formula application (UI accepts human-friendly percentages)
- Persistence behavior:
  - browser-local state key: `damage-calc-state-v1`
  - all `Input_*` fields are restored on load
  - state saves on submit and debounced `input/change` events
  - `Reset` clears local storage state for this page
- Output/diagnostics behavior:
  - result card includes `FormulaVersion` badge (current default: `excel-dmgcalc-v1`) to identify formula revision used
  - diagnostics panel surfaces workbook helper internals (including branch path numbers and interruption range)

## Guild Battle Sheet (`/GuildBattleSheet`)
- Purpose: public monthly recommendation sheet for the current guild battle element/damage type using the shared Gear Search weapon dataset.
- Entry points:
  - UI page: `Pages/GuildBattleSheet.cshtml`
  - Page model: `Pages/GuildBattleSheet.cshtml.cs`
  - Core service: `Services/GuildBattleSheetService.cs`
  - Request/result contracts: `Models/GuildBattleSheetModels.cs`
- Data/config behavior:
  - month definitions load from `data/guildBattleSheets.json`
  - newest configured month is selected by default when no `id` query parameter is provided
  - battle selection can be deep-linked with `/GuildBattleSheet?id=YYYY-MM`
  - month definitions also support optional `conditionalMechanics`; these only affect scoring when `Debug mode` is enabled
- Generation behavior:
  - recommendation pool is restricted to items whose `WeaponSearchItem.EquipmentType` is `Featured`
  - `topPicks` pins featured weapons at the top of main recommendations
  - `hiddenWeapons` suppresses named featured weapons from all generated sections
  - main ranking supports two modes:
    - `Traditional`: pool-first ranking across all featured weapons
    - `Character`: at most one main recommendation per character, with battle-fit DPS anchors preferred before support pivots
  - main ranking favors pinned picks, matching element, matching battle type, explicit fight-facing support/debuff effects, and then raw damage percent / matching attack stat
  - scoring now separates party-facing support, boss debuff setup, and self-only DPS prep into different signals
  - self-only support effects on off-fit weapons do not qualify as main featured fight utility; this includes self-only battle buffs and self-only element amplification / exploit-weakness style effects
  - target breadth is part of scoring, so broader ally support and broader enemy-targeting setup can outrank narrower variants
  - DPS characters without a battle-fit main hand can still appear in Character mode only if they re-qualify as support-style pivots through fight-facing utility
  - secondary sections are built deterministically from `EffectTags` and passive/R-ability names:
    - `<Element> Potency`
    - featured physical/magical sub-weapon suggestions
    - `<Element> Resistance Down`
    - `<Element> Damage Up`
    - `<Element> Damage Bonus`
  - the lower utility sections (`Resistance Down`, `Damage Up`, and damage bonus/weapon boost coverage) search the wider visible pool rather than staying featured-only
- UI behavior:
  - month dropdown with newest-first ordering
  - `Traditional` / `Character` mode selector only affects the main featured recommendations section
  - optional `Debug mode` query flag/UI toggle to expose ranking reasons per main recommendation
  - render path reuses shared item art URLs plus `PreviewImageUrl` for larger modal previews, following the same large-image preference path used by Gear Search
  - write-up panel prefers manual `writeup` lines from config and falls back to compact auto-generated callouts when absent
  - character-mode main cards can show `DPS Package`, `Support Package`, or `Support Pivot` badges
  - main recommendation cards now show a compact score pill plus an always-visible `Included because:` summary, with relevant customization details surfaced when they help explain the match

## Gear Search (`/GearSearch`)
- Purpose: searchable gear index with rich filters and detail views.
- Data source: `WeaponSearchDataService` + catalog enrichment.
- Results UI uses card-based rendering across mobile and desktop breakpoints.
- Gear Search now includes a top-level `Buff/Debuff Notes` button that opens an in-page Bootstrap modal with ally-vs-enemy tier tables and special status notes for quick reference during gear review.
- The modal content is mirrored in `docs/notes/buff-debuff-reference.md` so the reference remains readable outside the UI as well.
- Quick Filters now include fixed `Earth/Fire/Ice/Lightning/Water/Wind` + `Phys.` / `Mag.` combo pills for common searches.
- Quick Filters also include `Circle`, `Triangle`, `X`, and `Diamond` sigil shortcut pills that toggle the existing advanced sigil checkbox state instead of introducing a separate sigil filter path.
- Element/damage-type quick filters are implemented as explicit combo filters in the page script rather than by just toggling independent advanced checkboxes, so multiple selected quick filters preserve pair-wise matching instead of widening into a cross-product.
- Element values in result cards now render with matching elemental/non-elemental icons, plus the shared heal icon for `Heal`, from `wwwroot/images` using the same compact inline treatment as the stat icon row.
- Compare modal scraping now reuses the rendered element stack HTML (with a `data-element` fallback) and the full stat-stack markup so compared weapons keep the same element/stat presentation instead of losing it or flattening it during extraction.
- Sigil values in result cards now render with matching `ui_icon_sigil_*` assets in compact icon capsules, and materia support levels display as `I` / `II` instead of `x1` / `x2`.
- Result headers now use a dual-identity accent system: character theme variables remain the structural accent for portrait/border/header treatments, while element theme variables drive the weapon-name underline, element pill tint, and subtle top-corner flare across desktop banner rows and mobile card headers.
- Metadata pills under the weapon title now share normalized sizing/alignment so equipment, element, and type labels read as a consistent set.
- Gear Search now resolves a dedicated weapon/outfit image URL per item via `GearImageCatalog`, backed by `data/gearImages.json`, with `ui_icon_weapon.png` and `ui_icon_outfit.png` as the default placeholders.
- Desktop banner rows, mobile card headers, compare modal headers, and the `View Levels` snapshot header all consume the same resolved image URL so a single manifest backfill updates every Gear Search surface.
- Clicking any rendered gear-art thumbnail now opens an in-page preview modal that prefers `images/weapons/lg` or `images/outfits/lg` variants through `GearImageCatalog.ResolvePreviewImageUrl`, while falling back to the standard resolved art path when no large asset exists.
- The `View Levels` snapshot modal header reuses the same portrait/accent treatment and now applies the same iconized metadata pill styling plus element-reactive accent variables while deriving element/type values from the selected table row.
- Includes View Levels modal with dynamic OB/level snapshots, with the max level resolved from loaded FF7EC weapon/release data rather than hard-coded in the page.
- Customization unlock note: customizations unlock once the selected weapon level reaches 80, including 5★/OB0 weapons.
- Ability/customization effect text resolves localized status-condition/status-change names from FF7EC effect tables so the UI does not fall back to raw internal IDs for mapped effects.
- Passive-skill (`R Ability`) effects are resolved from FF7EC passive data tables using current passive points:
  - `SkillPassive` → `SkillPassiveLevel` (point-to-level mapping)
  - `SkillPassiveEffectGroup` → `SkillPassiveEffectLevel` (level-specific effect values)
  - localized description templates (`Localization/en.json`) with value/coefficient substitution
- Desktop cards now show resolved `R Ability` effect details inline; mobile/touch behavior keeps tap/click-friendly affordances.
- View Levels modal now renders each `R Ability` as:
  - first line: `+points SkillName`
  - subsequent lines: one-or-more resolved passive effect descriptions
- Advanced Search includes `Sub-R Abilities` filter options built from normalized passive-effect categories.
- Sub-R filtering is semantic key matching (not substring matching), so similarly named effects (for example `HP` vs `HP Gain`) remain distinct.

## Enemy Stats (`/EnemyStats`)
- Purpose: search by boss or stage and inspect enemy details.
- Results render as cards (single-column on smaller screens, 3-column on desktop).
- `Show Details` opens a modal so details do not shift page layout inline.
- Search suggestions use a custom typeahead dropdown optimized for mobile interactions.
- Stage-name match behavior:
  - `Stage` field shows stage
  - `Name` field shows enemy
  - `Level` shows enemy level
- Enemy-name match behavior keeps stage as `N/A`.
- Immunities panel is status-effect-focused; buff/debuff resistance is separate.

## Data Diagnostics (`/DataDiagnostics`) [Leadership]
- Purpose: validate whether weapon/costume entries are enriched from GearSearch data.
- Helps identify missing enrichment/potency/R-ability data.
- Also scans local gear-art coverage for both standard and large folders: `images/weapons`, `images/outfits`, `images/weapons/lg`, and `images/outfits/lg`.
- Includes a leadership-triggered full UnknownX7 data reload action (`OnPostReloadData`) that calls `WeaponSearchDataService.ReloadData()`.
- Reload flow also re-enriches `WeaponCatalog` from refreshed GearSearch snapshots (`WeaponCatalog.RefreshFromGearSearch()`) so diagnostics and downstream pages reflect newly loaded data without app restart.
- Reload metadata (`LastLoadedUtc`, `ReloadCount`) is displayed on page.

## Support Team Builder (`/SupportTeamBuilder`)
- Purpose: build ranked support-team weapon + outfit assignments from local UnknownX7 data without Google Sheets.
- Data source: `WeaponSearchDataService` (`external/UnknownX7/FF7EC-Data`) via `SupportTeamBuilderService`.
- UI includes a beta notice banner and an external-link button to the original reference tool (`https://diogocastro.com/ff7ec-support-team-builder/`) for parity checks.
- Input model:
  - dynamic list of effect filters (`effect`, `range`, `min base potency`, `min max potency`)
  - maximum character count (`1-3`)
  - must-have / exclude character sets
  - owned-OB selections for weapons + owned/not-owned selections for outfits from browser-local state
- Search/ranking behavior mirrors original support-builder precedence:
  - build assignments by folding both weapon and outfit matches into team candidates
  - reject assignments exceeding 2 weapons per character or max character cap
  - reject assignments with more than one outfit on the same character
  - score order: `max potency` desc, `character count` asc, `weapon count` asc, `base potency` desc
  - outfit potencies contribute to base/max score totals
  - remove duplicate teams by combined `weapon-name set + outfit-name set`
- Persistence:
  - browser-local state key: `support-team-builder-state-v1`
  - stores per-weapon owned OB selections and per-outfit owned selections, posted as JSON (`OwnedObJson`, `OwnedOutfitJson`) on search
- Interaction details:
  - changing an `Owned OB` or outfit `Owned/Not Owned` selector auto-submits the search form so ranked-team output refreshes immediately
  - effect options include server-computed potency-applicability metadata derived from explicit effect-line `[Pot]` / `[Max Pot]` tags across loaded entries
  - when a selected effect has no explicit potency metadata, that filter row auto-sets `Min Base` and `Min Max` to `Low`, disables both dropdowns, and shows an inline explanatory note
  - matching-weapon cards include a `View details` modal trigger
  - matching-outfit cards are shown in a parallel `Matching Outfits by Filter` panel
  - ranked-team rows expose both main-hand and off-hand weapons as modal triggers into the same weapon-details dialog
  - ranked-team rows render selected outfit name after weapons when present
  - weapon-details modal now includes a customization section under ability text

## Support Team Builder Vue Beta (`/SupportTeamBuilderVue`)
- Purpose: phased reactive migration of Support Team Builder using a Vue client while preserving backend scoring/matching parity.
- Host pattern: Razor page shell + Vue app mounted client-side (hybrid approach).
- API handlers on same page model (`SupportTeamBuilderVue.cshtml.cs`):
  - `GET ?handler=Options` -> `SupportTeamBuilderOptionData`
  - `POST ?handler=Search` -> `SupportTeamBuilderResponse`
- Backend logic remains centralized in `SupportTeamBuilderService` (no ranking logic duplicated in client).
- Client behavior:
  - in-page fetch updates for search results and ownership changes (no full postback reload)
  - local storage key reuse: `support-team-builder-state-v1`
  - same potency-applicability UX rules and Exploit Weakness non-potency override behavior
  - same matching panels, ranked-team rendering, and weapon/outfit details modal content

## Interactive Team Builder (`/InteractiveTeamBuilder`)
- Purpose: hand-build a 3-character team from your own armory and see its estimated damage live, scored by the same V2 engine as Player Power Analyzer V2.
- Host pattern: Razor page shell + Vue 3 (CDN, no build step) mounted client-side; Bootstrap 5.1.
- Files: `Pages/InteractiveTeamBuilder.cshtml` (+ `.cshtml.cs`), scoring/catalog logic in `Services/PlayerPowerAnalyzerV2Service.InteractiveTeamBuilder.cs`.
- Data source: the browser-local inventory (`player-inventory-state-v1`) is POSTed to the server, which returns an **owned-only** catalog; gear options are restricted to what the player owns.
- Team model: 3 character cells, each with main hand, off hand, ultimate weapon, main costume, 3 sub weapons, and 2 sub costumes.
  - Selecting a character already used elsewhere MOVES it (preserving picks) and resets the other cell.
  - Main/off/ult/costumes are character-specific; sub weapons may be any character's.
  - Weapons are de-duplicated team-wide (selecting a weapon used elsewhere clears the other slot).
- Pickers: "Choose Weapon" / "Choose Costume" open a modal showing ability + passive R-abilities (main/off/ult/main-costume show ability + passives; subs show passives). Off-hand passives count at half, ultimate at full. Modal supports search-by-ability-text, element quick-filters, and an R-ability checkbox filter (Any/All). In-use weapons/costumes are flagged; All-Allies weapon passives are shown as affecting the other characters; customization-added passives are included.
- API handlers (`InteractiveTeamBuilder.cshtml.cs`):
  - `GET ?handler=Catalog` -> intrinsic fallback catalog
  - `POST ?handler=Catalog` (`{ inventory }`) -> inventory-aware owned-only catalog (passives incl. customizations)
  - `POST ?handler=Score` (`{ inventory, team }`) -> `ScoreFixedTeam` result
- Results panel: score (relative model index, comparable to Analyzer V2 — **not** literal in-game damage, with an in-panel note saying so), per-character PATK/MATK/score, readable buffs/effects and debuffs (element-aware display names, deduped), per-character + team-wide R-ability levels with a breakpoint-chart modal, and a quick copy-text block.
  - **True attack stats:** when the player has entered Character Stats (see Player Inventory Management), the per-character PATK/MATK show the **total attack stat** = `floor((base + character/role stream + weapon) × (1 + Highwind%) × passive multiplier)`, where weapon is the per-slot-weighted sum (main+ult full, off+sub half — already in `TotalPatk`/`TotalMatk`), and the passive multiplier folds the always-on **Boost PATK / MATK / ATK** R-abilities (self + team-wide All-Allies; additive within a family, multiplicative across; Boost ATK boosts both). The ITB sends `player-character-stats-v1` as `characterStats` in the score POST; `ScoreFixedTeam` returns `AttackPatk`/`AttackMatk`/`HasCharacterStats`. Validated against an in-game loadout (lands a little under in-game = the unlogged weapon branding). Characters without entered stats show a weapons-only value marked `*`.
  - **Est. Dmg column (estimated average damage):** when a character has real attack stats, the table also shows the estimated average damage of one cast against a standard reference enemy via `EstimateAverageDamage(...)` (in `PlayerPowerAnalyzerV2Service.InteractiveTeamBuilder.cs`). It feeds the real `AttackPatk`/`AttackMatk` into the literal `DamageCalcService` model against `ReferenceEnemyDefense = 100` (PDEF/MDEF) and `ReferenceEnemyElementalResistanceModifier = -100` (a ×2.0 elemental layer). The carry ability is selected as the equipped damage weapon (main/off/ult/sub with `DamagePercent > 0`) matching the enemy weakness element → else highest `DamagePercent` → else a `NoDamageWeaponFallbackPotency` of 300%. It applies the character's ability-potency passive layer (`SumAbilityPotencyPercents`: self / All-Allies / Phys.-Mag. / elemental, breakpoint-resolved), the account-wide **Boost Wpn. C. Ability Pot.** Highwind line (`characterStats.GetHighwind("wpnCAbilityPot")` → `HighwindWeaponPotencyBonus`), and the team's detected buffs/debuffs (`ApplyTeamEffectsToRequest`). Returns `EstimatedAverageDamage` (rounded) or `null`; the UI renders `—` when null (no Character Stats). It is approximate — branding and exact rotation specifics aren't modeled, so it sits slightly conservative. The team Score stays a relative model index; Est. Dmg is the absolute estimate.
- **Save / Load / Delete builds (client-side):** the top action row has Save and Load buttons. Saved builds persist to localStorage `itb-saved-teams-v1` as `{ id, name, team, battle, savedAt }` (gear/battle selections only — inventory + Character Stats apply fresh on load). Save opens a name modal; a name collision (case-insensitive) shows an overwrite warning and the primary button becomes "Overwrite" (the record keeps its `id`). Load opens a modal listing builds (name + character summary + `savedAt`); selecting one calls `applyLoadTeam` to restore `team`+`battle` (padded/normalized into 3 cells) and the `[team, battle]` watcher re-scores. If the current build has content, load routes through a confirmation banner first. Delete (per row) opens a separate confirmation modal stacked above the Load modal (`z-index` 1070/1065). A cross-tab `storage` event on the key refreshes `teams.saved`; Escape closes the modals top-down. All in `Pages/InteractiveTeamBuilder.cshtml` — no server/engine changes.
- Scoring parity: `ScoreFixedTeam` reproduces analyzer scores; verified against the byte-identical repro and known anchor builds.

## Player Inventory Management (`/PlayerInventoryManagement`)
- Purpose: track owned weapons/costumes (and their level/overboost) in the browser; feeds the analyzers and the Interactive Team Builder.
- Persistence: browser-local `player-inventory-state-v1` (`{ weapons: { <id>: { ownership, level } }, costumes: { <id>: { owned } } }`); weapon `ownership` is `do-not-own` / `3-star` / `4-star` / `5-star` / `ob1..ob10`, ultimates use `own`.
- Import options:
  - **Import** (JSON) — load a previously exported inventory file.
  - **Paste Import** — single textarea accepting CSV/TSV or simple `Name - OB - Level` lines, with header auto-detection, per-row preview, and merge/replace modes.
  - **Import from Sheet** — three labeled paste boxes (Weapons / Ultimate Weapons / Costumes) for the community Google tracking sheet's tabs; see below.
  - **SOLDIER Survey Quick View** — compares a configured survey sheet's gear columns against saved inventory.
- Import from Sheet behavior:
  - Each box accepts the tab's pasted columns (tab-separated, with a blank spacer column + padding rows that are skipped): Weapons = `Overboost · Level · Weapon`; Ultimates = `Owned · Lv · Weapon` (the `[N Uses]` suffix is stripped); Costumes = `Owned · Gear`.
  - Names resolve against the live catalog by exact normalized name (reusing the page's structured paste parser; `gear` is recognized as a name header). If a name maps to more than one catalog entry (a rerun/variant), it is applied to ALL matches and noted, never dropped.
  - **Apply = replace the entire saved inventory**: both maps are wiped and rebuilt from the boxes; anything not listed becomes Do Not Own / Not Owned. A Preview step shows per-tab matched/owned/unmatched counts and warns when a box is left empty (that category would be wiped).
  - Guarded by `Tests/SheetImportNameMatchingTests.cs`, which resolves every sample name against the real catalog.
- **Character Stats** (panel above the catalog + "Enter Character Stats" modal): records per-character stat inputs so future damage estimates can use real attack stats. Stored separately in browser-local `player-character-stats-v1`.
  - Account-wide **Highwind** bonus: six percentages (HP, PATK, MATK, PDEF, MDEF, HEAL).
  - Per character: a **level**, then three stat blocks — **Base**, **Character Stream**, **Role Stream**.
    - **Base auto-fills (read-only) from the level** via `?handler=CharacterStatDefaults` → `CharacterBaseStatsService` (loads `CharacterLevel.json`; character name→`CharacterId` from the weapon catalog). It's the small raw per-level value, not the big in-game "Base Stats" number.
    - **Character Stream / Role Stream auto-default to their max** (every growth-board node unlocked): Character Stream = sum of GrowthBoard type-1 nodes, Role Stream = sum of type-5 nodes (`GrowthBoardGroup.json` + `GrowthBoardNode.json`). These reproduce the in-game stream values exactly. They stay editable (lower them if the trees aren't fully unlocked); a "Fill streams from max" button re-applies, and "Reset to Defaults" restores base+max.
  - The modal shows a live **Computed Total** per stat = `floor((Base + Character Stream + Role Stream) × (1 + Highwind%))`. The game floors, so this reproduces the in-game "Base Stats" total exactly (validated against a known character in `CharacterBaseStatsTests`). Labelled *approximate* — final damage is an average that omits some in-game effects (e.g. memoria).
  - The Highwind section also records three Highwind-only **ability-potency** lines alongside the six stat bonuses: **Boost Wpn. C. Ability Pot.**, **Boost Mat. C. Ability Pot.**, and **Boost Limit Ability Pot.** Only `Boost Wpn. C. Ability Pot.` is currently consumed downstream — the Interactive Team Builder reads it (`characterStats.GetHighwind("wpnCAbilityPot")`) as the weapon command-ability potency bonus in its damage estimate. The materia/limit lines are stored for reference only (materia and limit abilities are outside the team-builder damage estimate).
  - **Wired into the Interactive Team Builder (Phase 2 done):** total PATK/MATK = `floor((intrinsic + weapon stats) × (1 + Highwind%) × passive multiplier)` and an estimated per-cast damage now appear in the ITB results table (see Interactive Team Builder above). Weapon **branding** (RNG per-weapon PATK/MATK) is still not recorded and is the main expected source of residual error (it makes both numbers sit slightly conservative).

## Should I Attack (`/ShouldIAttack`)
- Purpose: recommend `Attack now` vs `Hold` for one selected player using simulation evidence.
- Data source: selected guild sheet from `GoogleSheets:GuildBattleSheets`.
- Day handling:
  - auto-detected suggestion based on attempt history and remaining hit logic
  - user-overridable day input (`Day 1-3`)
- Run orchestration:
  - executes `25` simulations per analysis
  - dispatcher path is attempted first; engine fallback is used when dispatcher is unavailable/fails
  - selected player is prioritized in dispatcher stage assignments and player list ordering before simulation
- Recommendation behavior:
  - standard mode: run selected by earliest selected-player attack, then resets desc, then final HP sum asc
  - immediate mode: prefers runs where selected player is first non-reset attack when available, otherwise falls back to all-run ranking by clears desc, final HP sum asc, points desc
  - immediate recommendation stage uses selected run's first player attack stage when available (run-aligned), with heuristic fallback stage retained for transparency
- UI behavior:
  - confirmation modal before analysis submit
  - player-loading indicator after guild selection
  - recommendation summary includes strict first-hit status, stage-hit summary, run source (dispatcher/fallback), and simulation caveat warning

## Should I Attack Bulk Diagnostics (`/ShouldIAttackBulk`) [Leadership]
- Purpose: leadership batch diagnostics for a selected guild sheet across all parsed players.
- Produces per-player recommendation rows and aggregate counts (attack now, fallback usage, missing recommendations).
- Intended for validation and operational review rather than direct end-user decision flow.

## Unlock (`/Unlock`)
- Shared password gate for leadership-only pages.
- Creates signed unlock cookie valid for configured duration.

## Home (`/`)
- Card index of all major tools.
- Indicates leadership-only pages with lock icons.

## Legacy Utility Service
- `CsvProcessor` and `PowerLevelCalculator` exist as generic CSV/power utilities.
- Core production ranking logic currently runs through GB20 ingestion + `TeamOptimizer` path.
