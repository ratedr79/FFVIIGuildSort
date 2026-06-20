using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    // Interactive Team Builder backend: score one MANUALLY-CHOSEN fixed 3-character team and return the SAME
    // number the V2 analyzer reports for that exact loadout. It reuses the engine's slot/variant construction
    // (GetOrCreateWeaponSlot / GetOrCreateCostumeSlot / ComposeCharacterVariantCandidate), credits the user's
    // FIXED subs through the same BuildVariantsWithSubPassivesCredited path the greedy uses, and computes the
    // team Score via the shared ScoreFinalizedTeamCore — so a hand-picked team reproduces the analyzer exactly.
    public sealed partial class PlayerPowerAnalyzerV2Service
    {
        // Built once on first request and reused (the catalog is roster-wide and immutable for the process: it
        // depends only on the static weapon/costume data, not on any per-request inventory). The page serves this
        // per GET, so caching avoids re-resolving every item's passives on every page load. This is the
        // NO-INVENTORY (intrinsic-only) fallback served by OnGetCatalog().
        private volatile InteractiveTeamBuilderCatalog? _interactiveTeamBuilderCatalog;
        private readonly object _interactiveTeamBuilderCatalogLock = new();

        // Inventory-aware catalogs, keyed by the raw inventory string. Each entry resolves every OWNED weapon's
        // passives (intrinsic + the customization R-ability the scorer credits) at the player's owned OB/level —
        // so the picker modal / R-ability filter show exactly what ScoreFixedTeam credits. Cached per inventory
        // string so repeated page loads with the same inventory are fast (bounded to a few recent inventories).
        private readonly ConcurrentDictionary<string, InteractiveTeamBuilderCatalog> _inventoryAwareCatalogCache =
            new(StringComparer.Ordinal);
        private const int InventoryAwareCatalogCacheLimit = 8;

        // Build the per-character slot catalog for the page (all weapon/ultimate/costume options across the roster).
        // The client filters this to owned items using its localStorage inventory. Each item also carries its active
        // ABILITY plus its passive R-abilities resolved at FULL and HALF value through the SAME engine breakpoint
        // path the scorer uses, so a picker modal can show exactly what ScoreFixedTeam would credit.
        public InteractiveTeamBuilderCatalog BuildInteractiveTeamBuilderCatalog()
        {
            var cached = _interactiveTeamBuilderCatalog;
            if (cached != null)
            {
                return cached;
            }

            lock (_interactiveTeamBuilderCatalogLock)
            {
                if (_interactiveTeamBuilderCatalog != null)
                {
                    return _interactiveTeamBuilderCatalog;
                }

                var catalog = new InteractiveTeamBuilderCatalog();
                var characters = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in _weaponSearchDataService.GetWeapons())
                {
                    var isCostume = string.Equals(item.EquipmentType, "Costume", StringComparison.OrdinalIgnoreCase);
                    string slot;
                    if (isCostume)
                    {
                        slot = "costume";
                    }
                    else if (string.Equals(item.EquipmentType, "Ultimate", StringComparison.OrdinalIgnoreCase))
                    {
                        slot = "ultimate";
                    }
                    else
                    {
                        slot = "weapon";
                    }

                    if (!string.IsNullOrWhiteSpace(item.Character))
                    {
                        characters.Add(item.Character);
                    }

                    // Costume passives resolve their labels/% through the resolved-effect (literal-%) path, exactly
                    // like CreateCostumeSlot's BuildPassiveContributionMap(..., preferResolvedEffectLabels: true);
                    // weapon passives use the breakpoint-table path. MaxPassiveSkills are the item's OWN intrinsic
                    // R-abilities at max OB (same source CreateWeaponSlot/CreateCostumeSlot read).
                    var passives = ResolveCatalogItemPassives(item.MaxPassiveSkills, preferResolvedEffectLabels: isCostume);

                    catalog.Items.Add(new InteractiveTeamBuilderCatalogItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Character = item.Character,
                        Slot = slot,
                        EquipmentType = item.EquipmentType,
                        Element = item.Element,
                        AbilityType = item.AbilityType,
                        DamagePercent = item.DamagePercent,
                        // Costumes have no active command ability; weapons/ultimates are identified by the weapon
                        // name (FF7EC has no separate ability-name field). Description is the rendered ability text.
                        Ability = isCostume ? string.Empty : item.Name,
                        AbilityDescription = isCostume ? string.Empty : (item.AbilityText ?? string.Empty),
                        Passives = passives
                    });
                }

                catalog.Characters = characters.ToList();
                _interactiveTeamBuilderCatalog = catalog;
                return catalog;
            }
        }

        // INVENTORY-AWARE catalog: identical to the no-arg catalog EXCEPT each OWNED weapon's Passives are resolved
        // from the player's OWNED snapshot (OB/level) through the SAME engine slot-construction path the scorer uses
        // (CreateWeaponSlot), so they include the customization-derived R-ability the scorer credits — the catalog's
        // per-item passives now equal what ScoreFixedTeam credits that item per-character. Owned costumes have no
        // customization layer, so they keep the intrinsic (MaxPassiveSkills) resolution. Unowned items (and any item
        // the inventory can't resolve to a snapshot) keep their intrinsic passives — the client filters them out
        // anyway. Falls back to the intrinsic-only catalog when no inventory is supplied. Cached per inventory string.
        public InteractiveTeamBuilderCatalog BuildInteractiveTeamBuilderCatalog(string localInventoryStateJson)
        {
            if (string.IsNullOrWhiteSpace(localInventoryStateJson))
            {
                return BuildInteractiveTeamBuilderCatalog();
            }

            if (_inventoryAwareCatalogCache.TryGetValue(localInventoryStateJson, out var cached))
            {
                return cached;
            }

            // Parse the inventory EXACTLY like ScoreFixedTeam does (same ParseInventoryState path). On any parse
            // failure, fall back to the intrinsic-only catalog rather than fail the page.
            var probe = new PlayerPowerAnalyzerV2Result();
            var inventoryState = ParseInventoryState(localInventoryStateJson, probe);
            if (inventoryState == null)
            {
                return BuildInteractiveTeamBuilderCatalog();
            }

            // Drive the SAME scoring context the fixed-team scorer constructs (no team specifics — the per-item
            // passive resolution and the customization pick are stable across battle contexts for the credited
            // passive points; the scorer's own probe confirmed identical credited points neutral vs Wind/Physical).
            var request = new PlayerPowerAnalyzerV2Request
            {
                SearchMode = PlayerPowerAnalyzerV2SearchMode.Full
            };
            var referenceTuningProfile = BuildReferenceTuningProfile(request);
            var weaponCache = new ConcurrentDictionary<string, SlotEvaluation>(StringComparer.OrdinalIgnoreCase);

            var catalog = new InteractiveTeamBuilderCatalog();
            var characters = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in _weaponSearchDataService.GetWeapons())
            {
                var isCostume = string.Equals(item.EquipmentType, "Costume", StringComparison.OrdinalIgnoreCase);
                string slot;
                if (isCostume)
                {
                    slot = "costume";
                }
                else if (string.Equals(item.EquipmentType, "Ultimate", StringComparison.OrdinalIgnoreCase))
                {
                    slot = "ultimate";
                }
                else
                {
                    slot = "weapon";
                }

                if (!string.IsNullOrWhiteSpace(item.Character))
                {
                    characters.Add(item.Character);
                }

                List<InteractiveTeamBuilderCatalogPassive> passives;
                if (!isCostume
                    && inventoryState.Weapons.TryGetValue(item.Id, out var weaponState)
                    && TryBuildOwnedWeaponCandidate(item, weaponState, out var owned))
                {
                    // OWNED weapon: resolve its FULL passive set (intrinsic + selected customization) through the
                    // SAME CreateWeaponSlot path the scorer uses, at full (Main, 1.0) and half (Off-hand, 0.5) value.
                    // The slot's flattened PassivePoints carry the customization R-ability the scorer credits, so the
                    // catalog passives equal what ScoreFixedTeam totals for that item per-character.
                    var role = CharacterRoleRegistry.GetRoleOrDefault(item.Character);
                    var fullSlot = GetOrCreateWeaponSlot(owned, role, request, referenceTuningProfile, "Main Weapon", 1.0, true, true, weaponCache);
                    var halfSlot = GetOrCreateWeaponSlot(owned, role, request, referenceTuningProfile, "Off-hand", 0.5, true, true, weaponCache);
                    passives = ResolveCatalogItemPassivesFromPointMaps(fullSlot.PassivePoints, halfSlot.PassivePoints);
                }
                else
                {
                    // Costume or unowned/unresolvable weapon: keep the intrinsic resolution (no customization layer).
                    passives = ResolveCatalogItemPassives(item.MaxPassiveSkills, preferResolvedEffectLabels: isCostume);
                }

                catalog.Items.Add(new InteractiveTeamBuilderCatalogItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Character = item.Character,
                    Slot = slot,
                    EquipmentType = item.EquipmentType,
                    Element = item.Element,
                    AbilityType = item.AbilityType,
                    DamagePercent = item.DamagePercent,
                    Ability = isCostume ? string.Empty : item.Name,
                    AbilityDescription = isCostume ? string.Empty : (item.AbilityText ?? string.Empty),
                    Passives = passives
                });
            }

            catalog.Characters = characters.ToList();

            // Bound the cache (drop an arbitrary existing entry when over the limit) so a long-lived process serving
            // many distinct inventories doesn't grow unbounded. The common case (a single player's inventory) is hot.
            if (_inventoryAwareCatalogCache.Count >= InventoryAwareCatalogCacheLimit)
            {
                foreach (var key in _inventoryAwareCatalogCache.Keys)
                {
                    _inventoryAwareCatalogCache.TryRemove(key, out _);
                    break;
                }
            }

            _inventoryAwareCatalogCache[localInventoryStateJson] = catalog;
            return catalog;
        }

        // Resolve an OWNED weapon candidate at its owned OB/level from the inventory's weapon state, mirroring the
        // exact ParseOwnedOverboost / NormalizeOwnedLevel / GetWeaponSnapshot path ScoreFixedTeam uses. Returns false
        // (and a null candidate) when the weapon isn't owned at a valid OB or has no snapshot.
        private bool TryBuildOwnedWeaponCandidate(WeaponSearchItem item, LocalInventoryWeaponState? weaponState, out OwnedWeaponCandidate owned)
        {
            owned = null!;
            var overboostLevel = ParseOwnedOverboost(weaponState?.Ownership);
            if (!overboostLevel.HasValue)
            {
                return false;
            }

            var level = NormalizeOwnedLevel(weaponState?.Level);
            var snapshot = _weaponSearchDataService.GetWeaponSnapshot(item.Id, overboostLevel.Value, level);
            if (snapshot == null)
            {
                return false;
            }

            owned = new OwnedWeaponCandidate(item, snapshot, overboostLevel.Value, level);
            return true;
        }

        // Build catalog passives from a slot's already-resolved FLATTENED passive-point maps (scoringLabel -> pooled,
        // slot-scaled points) at full and half value. These maps come straight from CreateWeaponSlot's PassivePoints,
        // which INCLUDE the selected customization R-ability — so each entry resolves through the SAME breakpoint path
        // (TryGetPassiveBonusValue) BuildCharacterPassiveTotals uses, guaranteeing catalog == scorer for that item.
        // Full/Half labels are matched by base label (costume literal-% marker stripped) so each row pairs correctly.
        private static List<InteractiveTeamBuilderCatalogPassive> ResolveCatalogItemPassivesFromPointMaps(
            IReadOnlyDictionary<string, int> fullPoints, IReadOnlyDictionary<string, int> halfPoints)
        {
            var result = new List<InteractiveTeamBuilderCatalogPassive>();
            if (fullPoints == null || fullPoints.Count == 0)
            {
                return result;
            }

            foreach (var fullEntry in fullPoints)
            {
                var scoringLabel = fullEntry.Key;
                var fullPts = fullEntry.Value;
                if (fullPts <= 0)
                {
                    continue;
                }

                var displayLabel = FormatPassiveDisplayLabel(scoringLabel);
                if (string.IsNullOrWhiteSpace(displayLabel))
                {
                    continue;
                }

                var halfLabel = MatchHalfPointLabel(scoringLabel, halfPoints);
                var halfPts = halfPoints != null && halfLabel != null && halfPoints.TryGetValue(halfLabel, out var hp) ? hp : 0;

                result.Add(new InteractiveTeamBuilderCatalogPassive
                {
                    Label = displayLabel,
                    Full = FormatCatalogPassivePointValue(scoringLabel, fullPts),
                    Half = halfPts > 0 && halfLabel != null ? FormatCatalogPassivePointValue(halfLabel, halfPts) : string.Empty,
                    FullPoints = fullPts,
                    HalfPoints = halfPts
                });
            }

            return result;
        }

        // Match a full-value scoring label to its half-value key. Weapon labels match directly; costume literal-%
        // labels bake the SLOT-SCALED % into the key, so fall back to matching by marker-stripped base label.
        private static string? MatchHalfPointLabel(string fullLabel, IReadOnlyDictionary<string, int>? halfPoints)
        {
            if (halfPoints == null || halfPoints.Count == 0)
            {
                return null;
            }

            if (halfPoints.ContainsKey(fullLabel))
            {
                return fullLabel;
            }

            var fullBase = StripCostumeLiteralMarker(fullLabel);
            foreach (var key in halfPoints.Keys)
            {
                if (string.Equals(StripCostumeLiteralMarker(key), fullBase, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return null;
        }

        // Resolve one (slot-scaled) scoring label + its pooled points to a short display string, EXACTLY as
        // BuildCharacterPassiveTotals does for the per-character panel: a signed % via the engine's breakpoint table
        // when it resolves, else a raw point total so non-%-shaped passives (e.g. Stream Phase Amp. Healing) still
        // surface. Same input + same resolver => the catalog string equals the per-character Value the scorer shows.
        private static string FormatCatalogPassivePointValue(string scoringLabel, int points)
        {
            if (points <= 0)
            {
                return string.Empty;
            }

            return TryGetPassiveBonusValue(scoringLabel, points, out var bonus)
                ? FormatSignedPercent(bonus)
                : $"+{points} pts";
        }

        // Resolve one item's passive R-abilities at FULL (slotMultiplier 1.0) and HALF (0.5) value, mirroring the
        // engine EXACTLY: it reuses BuildPassiveContributionMap (the same label resolution + costume literal-%
        // marker + the SAME point-halving: appliedPoints = floor(TotalPoints * slotMultiplier) BEFORE the lookup),
        // then resolves each (slot-scaled) label through TryGetPassiveBonusValue — the same breakpoint-table path
        // BuildPassiveFamilyTermsFromContributions/the damage model uses. Same item -> same value the scorer credits.
        private static List<InteractiveTeamBuilderCatalogPassive> ResolveCatalogItemPassives(
            IReadOnlyList<PassiveSkillTotal> passives, bool preferResolvedEffectLabels)
        {
            var result = new List<InteractiveTeamBuilderCatalogPassive>();
            if (passives == null || passives.Count == 0)
            {
                return result;
            }

            // The same per-R-ability contribution view the engine builds for full and half slots. Keyed by the
            // (possibly literal-%-marked) scoring label -> SkillName -> already-halved points, so each entry resolves
            // through TryGetPassiveBonusValue with EXACTLY the points the scorer would feed it for that slot.
            var fullContributions = BuildPassiveContributionMap(passives, 1.0, preferResolvedEffectLabels);
            var halfContributions = BuildPassiveContributionMap(passives, 0.5, preferResolvedEffectLabels);

            // Preserve a stable order (first appearance in the full-value map) for deterministic output.
            foreach (var fullEntry in fullContributions)
            {
                var label = fullEntry.Key;
                var displayLabel = FormatPassiveDisplayLabel(label);
                if (string.IsNullOrWhiteSpace(displayLabel))
                {
                    continue;
                }

                halfContributions.TryGetValue(MatchHalfLabel(label, halfContributions), out var halfBySkill);

                var full = FormatCatalogPassiveContribution(label, fullEntry.Value);
                var half = FormatCatalogPassiveContribution(MatchHalfLabel(label, halfContributions), halfBySkill);

                result.Add(new InteractiveTeamBuilderCatalogPassive
                {
                    Label = displayLabel,
                    Full = full,
                    Half = half,
                    // The raw points feeding Full/Half: summed per-provider points (the same totals
                    // FormatCatalogPassiveContribution resolves through the breakpoint tables).
                    FullPoints = SumProviderPoints(fullEntry.Value),
                    HalfPoints = SumProviderPoints(halfBySkill)
                });
            }

            return result;
        }

        // The costume literal-% path bakes the SLOT-SCALED % into the label (⟪LP:..⟫), so the full and half maps
        // use DIFFERENT keys for the same R-ability. Match the half-map key by base label (marker stripped).
        private static string MatchHalfLabel(string fullLabel, IReadOnlyDictionary<string, Dictionary<string, int>> halfContributions)
        {
            if (halfContributions.ContainsKey(fullLabel))
            {
                return fullLabel;
            }

            var fullBase = StripCostumeLiteralMarker(fullLabel);
            foreach (var key in halfContributions.Keys)
            {
                if (string.Equals(StripCostumeLiteralMarker(key), fullBase, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return fullLabel;
        }

        // Resolve a single (slot-scaled) scoring label + its per-skill points to a short display string, using the
        // ENGINE's TryGetPassiveBonusValue (breakpoint tables + costume literal-% marker). Different-named R-abilities
        // that map to the same buff label STACK (the damage model sums per-provider %s), so we resolve each provider's
        // points and sum the %s — the value EstimateTeamDamage would credit. Falls back to a points display for
        // passives that don't resolve to a % (e.g. Reprieve / stat-tier passives), so nothing is fabricated.
        private static string FormatCatalogPassiveContribution(string scoringLabel, Dictionary<string, int>? bySkill)
        {
            if (bySkill == null || bySkill.Count == 0)
            {
                return string.Empty;
            }

            var totalPercent = 0d;
            var anyResolved = false;
            var totalPoints = 0;
            foreach (var providerPoints in bySkill.Values)
            {
                totalPoints += providerPoints;
                if (TryGetPassiveBonusValue(scoringLabel, providerPoints, out var bonus))
                {
                    totalPercent += bonus;
                    anyResolved = true;
                }
            }

            if (anyResolved)
            {
                return FormatSignedPercent(totalPercent);
            }

            // No %-shaped breakpoint matched: surface the raw point total so the picker still shows the contribution.
            return totalPoints > 0 ? $"+{totalPoints} pts" : string.Empty;
        }

        // Sum the per-provider points for one resolved passive contribution (the same totals fed to the breakpoint
        // tables). Null/empty -> 0. Used for the catalog passive's FullPoints/HalfPoints.
        private static int SumProviderPoints(Dictionary<string, int>? bySkill)
        {
            if (bySkill == null || bySkill.Count == 0)
            {
                return 0;
            }

            var total = 0;
            foreach (var points in bySkill.Values)
            {
                total += points;
            }

            return total;
        }

        private static string FormatSignedPercent(double value)
        {
            var rounded = Math.Round(value, 2);
            var text = rounded.ToString("0.##", CultureInfo.InvariantCulture);
            return rounded >= 0 ? $"+{text}%" : $"{text}%";
        }

        // Score one manually-chosen fixed team against the player's inventory, reproducing the analyzer's number.
        public InteractiveTeamScoreResult ScoreFixedTeam(string localInventoryStateJson, InteractiveTeamSpec spec, string? characterStatsJson = null)
        {
            var result = new InteractiveTeamScoreResult();
            spec ??= new InteractiveTeamSpec();

            // Optional player-entered Character Stats (base + streams + Highwind) → enables true total attack stats.
            var characterStats = ParseCharacterStats(characterStatsJson);

            // Build a request whose context drives the SAME scoring (element/axis gating, refinement terms) the
            // analyzer uses. Full mode is irrelevant here (no search), but pin it so any internal mode branch reads
            // the deterministic path; subs are user-fixed so the sub-selection strategy never runs.
            var request = new PlayerPowerAnalyzerV2Request
            {
                EnemyWeakness = spec.EnemyWeakness,
                PreferredDamageType = spec.PreferredDamageType,
                TargetScenario = spec.TargetScenario,
                SearchMode = PlayerPowerAnalyzerV2SearchMode.Full
            };

            // --- 1. Parse inventory -> owned weapons / costumes at owned OB+level (mirrors Analyze's loop). ---
            var probe = new PlayerPowerAnalyzerV2Result();
            var inventoryState = ParseInventoryState(localInventoryStateJson, probe);
            if (inventoryState == null)
            {
                result.FailureReason = probe.FailureReason ?? "Inventory could not be read.";
                return result;
            }

            var ownedWeaponsByName = new Dictionary<string, OwnedWeaponCandidate>(StringComparer.OrdinalIgnoreCase);
            var ownedWeaponsById = new Dictionary<string, OwnedWeaponCandidate>(StringComparer.OrdinalIgnoreCase);
            var ownedCostumesByName = new Dictionary<string, OwnedCostumeCandidate>(StringComparer.OrdinalIgnoreCase);
            var ownedCostumesById = new Dictionary<string, OwnedCostumeCandidate>(StringComparer.OrdinalIgnoreCase);
            var allOwnedWeapons = new List<OwnedWeaponCandidate>();

            foreach (var item in _weaponSearchDataService.GetWeapons())
            {
                if (string.Equals(item.EquipmentType, "Costume", StringComparison.OrdinalIgnoreCase))
                {
                    if (inventoryState.Costumes.TryGetValue(item.Id, out var costumeState) && costumeState?.Owned == true)
                    {
                        var candidate = new OwnedCostumeCandidate(item);
                        ownedCostumesById[item.Id] = candidate;
                        ownedCostumesByName[item.Name] = candidate;
                    }

                    continue;
                }

                if (!inventoryState.Weapons.TryGetValue(item.Id, out var weaponState))
                {
                    continue;
                }

                var overboostLevel = ParseOwnedOverboost(weaponState?.Ownership);
                if (!overboostLevel.HasValue)
                {
                    continue;
                }

                var level = NormalizeOwnedLevel(weaponState?.Level);
                var snapshot = _weaponSearchDataService.GetWeaponSnapshot(item.Id, overboostLevel.Value, level);
                if (snapshot == null)
                {
                    continue;
                }

                var owned = new OwnedWeaponCandidate(item, snapshot, overboostLevel.Value, level);
                allOwnedWeapons.Add(owned);
                ownedWeaponsById[item.Id] = owned;
                ownedWeaponsByName[item.Name] = owned;
            }

            // --- 2. Build each character's base variant (main/off/ult/main-costume + sub-outfit bundle). ---
            var referenceTuningProfile = BuildReferenceTuningProfile(request);
            var weaponCache = new ConcurrentDictionary<string, SlotEvaluation>(StringComparer.OrdinalIgnoreCase);
            var costumeCache = new ConcurrentDictionary<string, SlotEvaluation>(StringComparer.OrdinalIgnoreCase);

            var characterSpecs = spec.Characters
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Character))
                .ToList();
            if (characterSpecs.Count == 0)
            {
                result.FailureReason = "No characters were specified for the team.";
                return result;
            }

            OwnedWeaponCandidate? ResolveWeapon(string? key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return null;
                }

                if (ownedWeaponsByName.TryGetValue(key, out var byName))
                {
                    return byName;
                }

                if (ownedWeaponsById.TryGetValue(key, out var byId))
                {
                    return byId;
                }

                result.UnresolvedItems.Add(key);
                return null;
            }

            OwnedCostumeCandidate? ResolveCostume(string? key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return null;
                }

                if (ownedCostumesByName.TryGetValue(key, out var byName))
                {
                    return byName;
                }

                if (ownedCostumesById.TryGetValue(key, out var byId))
                {
                    return byId;
                }

                result.UnresolvedItems.Add(key);
                return null;
            }

            var baseVariants = new List<CharacterBuildCandidate>();
            // Per-character ordered list of resolved fixed sub-weapons (max 3) to credit after the base variants.
            var fixedSubsByCharacter = new Dictionary<string, List<OwnedWeaponCandidate>>(StringComparer.OrdinalIgnoreCase);

            foreach (var charSpec in characterSpecs)
            {
                var character = charSpec.Character;
                var role = CharacterRoleRegistry.GetRoleOrDefault(character);

                var mainOwned = ResolveWeapon(charSpec.Main);
                if (mainOwned == null)
                {
                    result.Warnings.Add($"{character}: no resolvable main weapon (slot left empty / character skipped).");
                    continue;
                }

                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var mainSlot = GetOrCreateWeaponSlot(mainOwned, role, request, referenceTuningProfile, "Main Weapon", 1.0, true, true, weaponCache);
                usedNames.Add(mainSlot.Name);

                SlotEvaluation? offSlot = null;
                var offOwned = ResolveWeapon(charSpec.Off);
                if (offOwned != null && usedNames.Add(offOwned.Item.Name))
                {
                    offSlot = GetOrCreateWeaponSlot(offOwned, role, request, referenceTuningProfile, "Off-hand", 0.5, true, true, weaponCache);
                }

                SlotEvaluation? ultSlot = null;
                var ultOwned = ResolveWeapon(charSpec.Ultimate);
                if (ultOwned != null && usedNames.Add(ultOwned.Item.Name))
                {
                    ultSlot = GetOrCreateWeaponSlot(ultOwned, role, request, referenceTuningProfile, "Ultimate", 1.0, true, true, weaponCache);
                }

                SlotEvaluation? mainCostumeSlot = null;
                var mainCostumeOwned = ResolveCostume(charSpec.MainCostume);
                if (mainCostumeOwned != null && usedNames.Add(mainCostumeOwned.Item.Name))
                {
                    mainCostumeSlot = GetOrCreateCostumeSlot(mainCostumeOwned, role, request, referenceTuningProfile, "Main Outfit", 1.0, true, costumeCache);
                }

                // Sub-outfit bundle: mirror the production caller (GetOrCreateCostumeSlot "Sub Outfit" 0.5 false,
                // accumulate NonPassiveScore + PassivePoints, dedup by name).
                var subOutfits = new List<PlayerPowerAnalyzerV2ItemSlot>();
                var subOutfitPassivePoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var subOutfitScore = 0d;
                foreach (var subCostumeKey in charSpec.SubCostumes.Take(2))
                {
                    var subCostumeOwned = ResolveCostume(subCostumeKey);
                    if (subCostumeOwned == null || !usedNames.Add(subCostumeOwned.Item.Name))
                    {
                        continue;
                    }

                    var subEval = GetOrCreateCostumeSlot(subCostumeOwned, role, request, referenceTuningProfile, "Sub Outfit", 0.5, false, costumeCache);
                    subOutfits.Add(subEval.Slot);
                    subOutfitScore += subEval.NonPassiveScore;
                    AddPassivePoints(subOutfitPassivePoints, subEval.PassivePoints);
                }

                var variant = ComposeCharacterVariantCandidate(
                    character,
                    role,
                    mainSlot,
                    offSlot,
                    ultSlot,
                    mainCostumeSlot,
                    subOutfits,
                    subOutfitPassivePoints,
                    subOutfitScore,
                    usedNames,
                    request);
                baseVariants.Add(variant);

                // Resolve the FIXED sub-weapons for this character (max 3), excluding anything already equipped.
                var fixedSubs = new List<OwnedWeaponCandidate>();
                foreach (var subKey in charSpec.SubWeapons.Take(3))
                {
                    var subOwned = ResolveWeapon(subKey);
                    if (subOwned == null)
                    {
                        continue;
                    }

                    if (usedNames.Contains(subOwned.Item.Name))
                    {
                        continue;
                    }

                    fixedSubs.Add(subOwned);
                }

                fixedSubsByCharacter[character] = fixedSubs;
            }

            if (baseVariants.Count == 0)
            {
                result.FailureReason = "None of the specified characters had a resolvable main weapon.";
                return result;
            }

            // --- 3. Credit the FIXED sub-weapons exactly as FinalizeTeamCandidate's greedy does per pick. ---
            var characterOutputs = baseVariants.Select(v => v.ToOutput()).ToList();
            var characterOutputsByName = characterOutputs.ToDictionary(c => c.CharacterName, StringComparer.OrdinalIgnoreCase);
            var baseNonPassiveScoresByCharacter = baseVariants.ToDictionary(v => v.CharacterName, v => v.NonPassiveScore, StringComparer.OrdinalIgnoreCase);
            var passivePointsByCharacter = baseVariants.ToDictionary(v => v.CharacterName, v => new Dictionary<string, int>(v.PassivePoints, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            var subWeaponNonPassiveScoreByCharacter = baseVariants.ToDictionary(v => v.CharacterName, _ => 0d, StringComparer.OrdinalIgnoreCase);
            var selectedSubPassiveContributionsByCharacter = baseVariants.ToDictionary(
                v => v.CharacterName,
                _ => new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            // Items already equipped across the whole team cannot be re-used as a sub (matches the greedy's
            // usedItemNames exclusion, which is seeded from every variant's UsedItemNames).
            var teamUsedItemNames = new HashSet<string>(baseVariants.SelectMany(v => v.UsedItemNames), StringComparer.OrdinalIgnoreCase);

            foreach (var variant in baseVariants)
            {
                if (!fixedSubsByCharacter.TryGetValue(variant.CharacterName, out var subs))
                {
                    continue;
                }

                var role = variant.Role;
                var character = characterOutputsByName[variant.CharacterName];
                var assigned = 0;
                foreach (var subOwned in subs)
                {
                    if (assigned >= 3)
                    {
                        break;
                    }

                    if (!teamUsedItemNames.Add(subOwned.Item.Name))
                    {
                        continue; // already used somewhere on the team
                    }

                    // Sub-weapons are scored exactly as the greedy scores them: role = the equipper's effective
                    // sub-weapon role, slot "Sub Weapon", half value, no active/damage credit.
                    var subEval = GetOrCreateWeaponSlot(subOwned, variant.EffectiveSubWeaponRole, request, referenceTuningProfile, "Sub Weapon", 0.5, false, false, weaponCache);

                    character.SubWeapons.Add(subEval.Slot);
                    character.TotalPatk += subEval.Slot.Patk;
                    character.TotalMatk += subEval.Slot.Matk;
                    character.TotalHeal += subEval.Slot.Heal;
                    subWeaponNonPassiveScoreByCharacter[variant.CharacterName] += subEval.NonPassiveScore;
                    AddPassivePoints(passivePointsByCharacter[variant.CharacterName], subEval.PassivePoints);
                    MergePassiveContributions(selectedSubPassiveContributionsByCharacter[variant.CharacterName], subEval.PassiveContributions);
                    assigned++;
                }
            }

            // --- 4. Compute the team Score via the SHARED finalize core (byte-identical to the analyzer). ---
            var enabledTemplateNames = _teamTemplateCatalog.GetEnabledTemplates().Select(t => t.Name).ToList();
            var normalizedEnabledTemplateNames = enabledTemplateNames
                .GroupBy(NormalizeTemplateName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var teamCandidate = ScoreFinalizedTeamCore(
                baseVariants,
                characterOutputs,
                passivePointsByCharacter,
                baseNonPassiveScoresByCharacter,
                subWeaponNonPassiveScoreByCharacter,
                selectedSubPassiveContributionsByCharacter,
                request,
                referenceTuningProfile,
                normalizedEnabledTemplateNames);

            // Expose the raw damage headline and the refinement separately (Score = headline + refinement, with the
            // template 50% penalty already folded into teamCandidate.Score when no template matched).
            var creditedVariants = BuildVariantsWithSubPassivesCredited(baseVariants, selectedSubPassiveContributionsByCharacter);
            var rawDamage = request.PreferredDamageType != DamageType.Any
                ? EstimateTeamDamage(creditedVariants, request)
                : 0d;

            result.HasResult = true;
            result.Score = Math.Round(teamCandidate.Score, 2);
            result.RawDamage = Math.Round(rawDamage, 2);
            result.Refinement = Math.Round(teamCandidate.Score - rawDamage, 2);

            // Team-wide (All Allies) attack passives sum across every character and apply to each one.
            double teamAllyPatk = 0, teamAllyMatk = 0, teamAllyAtk = 0;
            foreach (var pp in passivePointsByCharacter.Values)
            {
                var (_, _, _, ap, am, aa) = SumAttackBoostPercents(pp);
                teamAllyPatk += ap;
                teamAllyMatk += am;
                teamAllyAtk += aa;
            }

            // Per-character output, ordered the same way the spec listed them.
            var outputsByName = teamCandidate.Characters.ToDictionary(c => c.CharacterName, StringComparer.OrdinalIgnoreCase);
            foreach (var charSpec in characterSpecs)
            {
                if (!outputsByName.TryGetValue(charSpec.Character, out var built))
                {
                    continue;
                }

                passivePointsByCharacter.TryGetValue(built.CharacterName, out var characterPassivePoints);

                // True total attack stat (when Character Stats exist for this character):
                //   floor((base + characterStream + roleStream + weapon PATK/MATK) × (1 + Highwind%) × passive mult).
                // Weapon totals (built.TotalPatk/Matk) already apply the per-slot weighting (main/ult full, off/sub half).
                // Passive mult folds the always-on Boost-PATK/MATK/ATK R-abilities (self + team-wide All-Allies).
                // Families are additive within a family, multiplicative across; Boost ATK boosts both PATK and MATK.
                // Branding (RNG per-weapon ATK) is not recorded → result sits a little under in-game.
                int? attackPatk = null, attackMatk = null;
                var hasStats = characterStats != null && characterStats.TryGetIntrinsic(built.CharacterName, "patk", out _);
                if (hasStats)
                {
                    var (selfPatk, selfMatk, selfAtk, _, _, _) = SumAttackBoostPercents(characterPassivePoints);
                    var atkFamily = (selfAtk + teamAllyAtk) / 100d;
                    var patkMultiplier = (1 + (selfPatk + teamAllyPatk) / 100d) * (1 + atkFamily);
                    var matkMultiplier = (1 + (selfMatk + teamAllyMatk) / 100d) * (1 + atkFamily);
                    attackPatk = characterStats!.ComputeAttackStat(built.CharacterName, "patk", built.TotalPatk, patkMultiplier);
                    attackMatk = characterStats.ComputeAttackStat(built.CharacterName, "matk", built.TotalMatk, matkMultiplier);
                }

                result.Characters.Add(new InteractiveTeamCharacterResult
                {
                    Name = built.CharacterName,
                    Role = built.Role.ToString(),
                    Patk = built.TotalPatk,
                    Matk = built.TotalMatk,
                    AttackPatk = attackPatk,
                    AttackMatk = attackMatk,
                    HasCharacterStats = hasStats,
                    FinalScore = built.Score,
                    Main = built.MainWeapon,
                    Off = built.OffHandWeapon,
                    Ult = built.UltimateWeapon,
                    Outfit = built.MainOutfit,
                    SubWeapons = built.SubWeapons.ToList(),
                    SubOutfits = built.SubOutfits.ToList(),
                    // The character's accumulated passive R-ability totals (points already slot-halved by the scorer),
                    // each resolved to its breakpoint Level + Value through the engine's own tables.
                    Passives = BuildCharacterPassiveTotals(characterPassivePoints)
                });
            }

            // Breakpoint charts for the "Show R. Ability Levels" modal: one per DISTINCT passive family across the
            // scored team (union of the characters' Passives labels), straight from the engine's breakpoint arrays.
            result.RAbilityCharts = BuildTeamRAbilityCharts(result.Characters);

            // Team active buffs/debuffs/effects (reuse the per-variant detected-effect state the damage model uses).
            PopulateTeamEffects(result, creditedVariants, request);

            // Phase 2b: estimated average damage per character (needs both the per-character attack stats AND the
            // team-wide buffs/debuffs just populated, so it runs as a second pass). Only characters with real attack
            // stats (Character Stats entered) get a number; the rest stay null.
            var highwindWeaponPotency = characterStats?.GetHighwind("wpnCAbilityPot") ?? 0d; // account-wide, % (e.g. 31)

            // Classify each character's ability-potency passives once, and sum the (All Allies) pool across the whole
            // team — so a team-wide ability-potency buff (e.g. a support's "Boost Ability Pot. (All Allies)") credits
            // every character's estimate, not only the one wearing it. Matched to each recipient's own cast at use.
            var selfPotencyByCharacter = new Dictionary<string, ScopedAbilityPotency>(StringComparer.OrdinalIgnoreCase);
            var teamAllAlliesPotency = new ScopedAbilityPotency();
            foreach (var ch in result.Characters)
            {
                passivePointsByCharacter.TryGetValue(ch.Name, out var chPassives);
                var breakdown = ClassifyAbilityPotency(chPassives);
                selfPotencyByCharacter[ch.Name] = breakdown.Self;
                teamAllAlliesPotency.Add(breakdown.AllAllies);
            }

            foreach (var ch in result.Characters)
            {
                var selfPotency = selfPotencyByCharacter.TryGetValue(ch.Name, out var sp) ? sp : new ScopedAbilityPotency();
                ch.EstimatedAverageDamage = EstimateAverageDamage(ch, selfPotency, teamAllAlliesPotency, spec, result.Buffs, result.Debuffs, highwindWeaponPotency);
            }

            // CopyText: one line per character, in the spec's order, omitting empty clauses.
            result.CopyText = BuildCopyText(characterSpecs, outputsByName);
            result.UnresolvedItems = result.UnresolvedItems.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        // Build one character's accumulated passive R-ability totals from the scorer's per-character points map
        // (scoringLabel -> already-slot-halved points). Each entry resolves its breakpoint Level + display Value
        // through the engine's OWN resolvers (ResolvePassiveLevel / TryGetPassiveBonusValue). Sorted by Points desc.
        // The scoring label is the lookup key (it carries the family/element/scope the resolvers classify on, plus the
        // costume literal-% marker); the user-facing Label is the marker-stripped display form. All-Allies passives are
        // kept here under the character (each character has its OWN instance), labeled "... (All Allies)".
        private static List<InteractiveTeamCharacterPassive> BuildCharacterPassiveTotals(Dictionary<string, int>? passivePoints)
        {
            var result = new List<InteractiveTeamCharacterPassive>();
            if (passivePoints == null || passivePoints.Count == 0)
            {
                return result;
            }

            foreach (var (scoringLabel, points) in passivePoints)
            {
                if (points <= 0)
                {
                    continue;
                }

                var displayLabel = FormatPassiveDisplayLabel(scoringLabel);
                if (string.IsNullOrWhiteSpace(displayLabel))
                {
                    continue;
                }

                var value = TryGetPassiveBonusValue(scoringLabel, points, out var bonus)
                    ? FormatSignedPercent(bonus)
                    : $"+{points} pts";

                result.Add(new InteractiveTeamCharacterPassive
                {
                    Label = displayLabel,
                    Points = points,
                    Level = ResolvePassiveLevel(scoringLabel, points),
                    Value = value
                });
            }

            return result
                .OrderByDescending(p => p.Points)
                .ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Build the breakpoint charts for the team: one per DISTINCT passive family present across all characters'
        // Passives (deduped by display Label), each carrying the full (breakpointPoints[], bonuses[]) table for that
        // family from the engine. Passives with no breakpoint table (costume literal-%, non-%-shaped) are skipped —
        // they have no chart to show. Charts are ordered by first appearance for deterministic output.
        private static List<InteractiveTeamRAbilityChart> BuildTeamRAbilityCharts(IReadOnlyList<InteractiveTeamCharacterResult> characters)
        {
            var charts = new List<InteractiveTeamRAbilityChart>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var character in characters)
            {
                foreach (var passive in character.Passives)
                {
                    if (!seen.Add(passive.Label))
                    {
                        continue;
                    }

                    // The display Label is the marker-stripped family label, which still carries the family/element/
                    // scope text the table router classifies on — so it routes to the same table the totals used.
                    if (TryBuildPassiveBreakpointChart(passive.Label, out var rows))
                    {
                        charts.Add(new InteractiveTeamRAbilityChart
                        {
                            Label = passive.Label,
                            Rows = rows
                        });
                    }
                }
            }

            return charts;
        }

        private void PopulateTeamEffects(InteractiveTeamScoreResult result, IReadOnlyList<CharacterBuildCandidate> variants, PlayerPowerAnalyzerV2Request request)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variant in variants)
            {
                foreach (var effect in GetDetectedEffectsForVariant(variant, request))
                {
                    // Polarity is intrinsic to the effect family (a Weapon Boost is always an ally buff);
                    // only fall back to the parsed target scope for families we don't explicitly classify.
                    var isDebuff = GetEffectDebuffPolarityByFamily(effect.FamilyKey)
                        ?? (effect.TargetScope == ActiveEffectTargetScope.SingleEnemy
                            || effect.TargetScope == ActiveEffectTargetScope.AllEnemies);

                    var displayName = string.IsNullOrWhiteSpace(effect.DisplayName) ? effect.Key : effect.DisplayName;

                    // Dedup by the readable identity (name + potency + polarity), not by source: the team
                    // either has an effect or it doesn't, same-family buffs share a cap in battle, and this
                    // guarantees a given effect renders once — never duplicated across both sections.
                    var potencyKey = effect.PotencyPercent.HasValue
                        ? ((int)Math.Round(effect.PotencyPercent.Value)).ToString(CultureInfo.InvariantCulture)
                        : string.Empty;
                    var dedupKey = string.Join("|", displayName, potencyKey, isDebuff ? "d" : "b");
                    if (!seen.Add(dedupKey))
                    {
                        continue;
                    }

                    var entry = new InteractiveTeamEffect
                    {
                        Key = effect.Key,
                        DisplayName = displayName,
                        Family = effect.FamilyKey,
                        Axis = effect.AxisKey,
                        Scope = effect.TargetScope.ToString(),
                        Source = effect.SourceName,
                        SourceType = effect.SourceType,
                        Potency = effect.PotencyPercent,
                        Kind = isDebuff ? "debuff" : "buff"
                    };

                    if (isDebuff)
                    {
                        result.Debuffs.Add(entry);
                    }
                    else
                    {
                        result.Buffs.Add(entry);
                    }
                }
            }
        }

        private static string BuildCopyText(
            IReadOnlyList<InteractiveTeamCharacterSpec> specs,
            IReadOnlyDictionary<string, PlayerPowerAnalyzerV2CharacterBuild> outputsByName)
        {
            var lines = new List<string>();
            foreach (var spec in specs)
            {
                if (!outputsByName.TryGetValue(spec.Character, out var built))
                {
                    continue;
                }

                var clauses = new List<string>();
                var main = built.MainWeapon?.Name;
                var off = built.OffHandWeapon?.Name;
                if (!string.IsNullOrWhiteSpace(main) && !string.IsNullOrWhiteSpace(off))
                {
                    clauses.Add($"{built.CharacterName} w/ {main} and {off}.");
                }
                else if (!string.IsNullOrWhiteSpace(main))
                {
                    clauses.Add($"{built.CharacterName} w/ {main}.");
                }
                else
                {
                    clauses.Add($"{built.CharacterName}.");
                }

                if (!string.IsNullOrWhiteSpace(built.MainOutfit?.Name))
                {
                    clauses.Add($"{built.MainOutfit!.Name} costume.");
                }

                if (!string.IsNullOrWhiteSpace(built.UltimateWeapon?.Name))
                {
                    clauses.Add($"{built.UltimateWeapon!.Name} UW.");
                }

                var subNames = built.SubWeapons.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                if (subNames.Count > 0)
                {
                    clauses.Add($"{JoinList(subNames)} for sub weapons.");
                }

                var subCostumeNames = built.SubOutfits.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                if (subCostumeNames.Count > 0)
                {
                    clauses.Add($"{JoinList(subCostumeNames)} for sub costumes.");
                }

                lines.Add(string.Join(" ", clauses));
            }

            return string.Join("\n", lines);
        }

        // "a", "a and b", "a, b, and c" (Oxford comma) — matches the spec's CopyText format.
        private static string JoinList(IReadOnlyList<string> items)
        {
            return items.Count switch
            {
                0 => string.Empty,
                1 => items[0],
                2 => $"{items[0]} and {items[1]}",
                _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
            };
        }

        // Sums the attack-STAT passive boosts from one character's passive points, split self vs (All Allies):
        //   Boost PATK / Boost MATK / Boost ATK (ATK applies to both PATK and MATK). Values are percents (e.g. 50).
        //   Ability-potency and defense boosts are damage/defense, NOT attack-stat boosts, so they're excluded.
        private static (double SelfPatk, double SelfMatk, double SelfAtk, double AllyPatk, double AllyMatk, double AllyAtk)
            SumAttackBoostPercents(Dictionary<string, int>? passivePoints)
        {
            double selfPatk = 0, selfMatk = 0, selfAtk = 0, allyPatk = 0, allyMatk = 0, allyAtk = 0;
            if (passivePoints != null)
            {
                foreach (var (scoringLabel, points) in passivePoints)
                {
                    if (points <= 0)
                    {
                        continue;
                    }

                    var label = FormatPassiveDisplayLabel(scoringLabel);
                    if (string.IsNullOrWhiteSpace(label) || label.Contains("Ability Pot", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // ability potency boosts damage, not the attack stat
                    }

                    if (!TryGetPassiveBonusValue(scoringLabel, points, out var pct) || pct == 0)
                    {
                        continue;
                    }

                    var ally = label.Contains("All Allies", StringComparison.OrdinalIgnoreCase);
                    if (label.Contains("Boost ATK", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ally) { allyAtk += pct; } else { selfAtk += pct; }
                    }
                    else if (label.Contains("Boost PATK", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ally) { allyPatk += pct; } else { selfPatk += pct; }
                    }
                    else if (label.Contains("Boost MATK", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ally) { allyMatk += pct; } else { selfMatk += pct; }
                    }
                }
            }

            return (selfPatk, selfMatk, selfAtk, allyPatk, allyMatk, allyAtk);
        }

        // ===== Phase 2b: estimated average damage (literal Shira DamageCalc model fed by real stats) =====
        // DamageCalcService is stateless (only static lookup tables), so one shared instance is safe to reuse.
        private static readonly DamageCalcService DamageEstimator = new();

        // Standard reference enemy (agreed): PDef/MDef 100; a moderate elemental weakness. -100 → PercentInputToDecimal
        // → (1 - (-1.0)) = ×2.0 elemental layer (the model's default -200 would be ×3.0).
        private const double ReferenceEnemyDefense = 100d;
        private const double ReferenceEnemyElementalResistanceModifier = -100d;
        private const double NoDamageWeaponFallbackPotency = 300d; // % — character with no damage-dealing weapon

        // Estimate one cast's average damage for a character, against the standard reference enemy. Returns null
        // unless the character has real attack stats (Character Stats entered). Carry ability = the equipped damage
        // weapon matching the enemy weakness element, else the highest-potency weapon, else a 300% fallback.
        private static double? EstimateAverageDamage(
            InteractiveTeamCharacterResult ch,
            ScopedAbilityPotency selfPotency,
            ScopedAbilityPotency teamAllAlliesPotency,
            InteractiveTeamSpec spec,
            IReadOnlyList<InteractiveTeamEffect> buffs,
            IReadOnlyList<InteractiveTeamEffect> debuffs,
            double highwindWeaponPotency)
        {
            // Absolute damage needs a real attack stat; without Character Stats we only have weapon-only PATK/MATK.
            if (ch.AttackPatk is not int patk || ch.AttackMatk is not int matk)
            {
                return null;
            }

            // --- Carry ability + potency: element-match (enemy weakness) → highest potency → 300% fallback. ---
            var weakness = spec.EnemyWeakness == Element.None ? null : spec.EnemyWeakness.ToString();
            var damageWeapons = new[] { ch.Main, ch.Off, ch.Ult }
                .Concat(ch.SubWeapons)
                .Where(w => w != null && w.DamagePercent > 0)
                .Select(w => w!)
                .ToList();

            PlayerPowerAnalyzerV2ItemSlot? carry = null;
            if (weakness != null)
            {
                carry = damageWeapons
                    .Where(w => string.Equals(w.Element, weakness, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(w => w.DamagePercent)
                    .FirstOrDefault();
            }
            carry ??= damageWeapons.OrderByDescending(w => w.DamagePercent).FirstOrDefault();

            var potency = carry?.DamagePercent ?? NoDamageWeaponFallbackPotency;
            var abilityType = carry?.AbilityType ?? (patk >= matk ? "Phys." : "Mag.");
            var castElement = carry?.Element ?? "Non-Elemental";
            var isMag = abilityType.Equals("Mag.", StringComparison.OrdinalIgnoreCase);
            var isMixed = abilityType.Equals("Phys./Mag.", StringComparison.OrdinalIgnoreCase);
            var physApplies = !isMag || isMixed; // "Phys." / fallback / mixed
            var magApplies = isMag || isMixed;
            var damageType = isMixed ? "Physical/Magical" : isMag ? "Magical" : "Physical";

            // --- Ability-potency passive layer (additive within the potency bundle). Self contributions are matched to
            //     this character's own cast; the team-wide (All Allies) pool is matched to this character as a recipient
            //     (general always, phys/mag by this attacker's axis, elemental by this cast's element). ---
            var elementalCast = ContainsElementName(castElement);
            var selfElemental = elementalCast && selfPotency.ByElement.TryGetValue(castElement, out var se) ? se : 0d;

            var req = new DamageCalcRequest
            {
                DamageType = damageType,
                PhysicalAttackStat = patk,
                MagicalAttackStat = matk,
                WeaponAbilityPotency = potency,
                HighwindWeaponPotencyBonus = highwindWeaponPotency, // "Boost Wpn. C. Ability Pot." Highwind line, % (e.g. 31)
                // Resolved fractions bypass the tier-string lookups AND their non-zero defaults.
                AbilityPotencyResolved = selfPotency.General / 100d,
                // Team-wide All-Allies ability potency (summed across the team, matched to this recipient) — percent.
                BoostAbilityPotAllAllies = teamAllAlliesPotency.MatchedTotal(physApplies, magApplies, castElement),
                PhysicalAbilityPotencyResolved = (physApplies ? selfPotency.Phys : 0d) / 100d,
                MagicalAbilityPotencyResolved = (magApplies ? selfPotency.Mag : 0d) / 100d,
                ElementalPotencyResolved = selfElemental / 100d,
                // Ability-dmg families are summed into the resolved values above → neutralize the model's non-zero defaults.
                OutfitAbilityBonus = 0,            // default 30
                MemoriaElementalPotencyBonus = 0,  // default 15
                MemoriaPhysicalAbilityBonus = 0,   // default 15
                // Standard reference enemy.
                EnemyPhysicalDefense = ReferenceEnemyDefense,
                EnemyMagicDefense = ReferenceEnemyDefense,
                EnemyElementalResistanceModifier = ReferenceEnemyElementalResistanceModifier,
            };

            ApplyTeamEffectsToRequest(req, buffs, debuffs, physApplies, magApplies, castElement);

            var damage = DamageEstimator.Calculate(req).AverageDamage;
            return double.IsFinite(damage) && damage > 0 ? Math.Round(damage) : null;
        }

        // Ability-potency contributions split by scope, so team-wide (All Allies) sources can be summed across the
        // whole team and applied to each recipient (matched to THAT recipient's cast) — the way attack-stat All-Allies
        // passives already are. General = element/axis-agnostic ability pot; Phys/Mag = axis-specific; ByElement =
        // elemental potency / "[Element] Ability Dmg" keyed by element. Values are percents.
        private sealed class ScopedAbilityPotency
        {
            public double General;
            public double Phys;
            public double Mag;
            public Dictionary<string, double> ByElement { get; } = new(StringComparer.OrdinalIgnoreCase);

            public void Add(ScopedAbilityPotency other)
            {
                General += other.General;
                Phys += other.Phys;
                Mag += other.Mag;
                foreach (var (element, value) in other.ByElement)
                {
                    ByElement[element] = (ByElement.TryGetValue(element, out var existing) ? existing : 0) + value;
                }
            }

            // The portion that applies to a given cast: general always, phys/mag by attacker axis, elemental only when
            // the cast deals that element. Returns a percent.
            public double MatchedTotal(bool physApplies, bool magApplies, string castElement)
            {
                var total = General;
                if (physApplies) { total += Phys; }
                if (magApplies) { total += Mag; }
                if (ContainsElementName(castElement) && ByElement.TryGetValue(castElement, out var elemental))
                {
                    total += elemental;
                }
                return total;
            }
        }

        private sealed class AbilityPotencyBreakdown
        {
            public ScopedAbilityPotency Self { get; } = new();      // the character's own; matched to its own cast
            public ScopedAbilityPotency AllAllies { get; } = new(); // team-wide; summed across the team, matched per recipient
        }

        private static readonly string[] PotencyElementNames = { "Fire", "Ice", "Lightning", "Water", "Wind", "Earth", "Holy", "Dark" };
        private static string? ExtractPotencyElementName(string label)
        {
            foreach (var element in PotencyElementNames)
            {
                if (label.Contains(element, StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }
            }
            return null;
        }

        // Classifies one character's ability-POTENCY passives (a DAMAGE multiplier layer, distinct from the attack-stat
        // Boost PATK/MATK handled by SumAttackBoostPercents) into Self vs (All Allies), each broken down by scope. The
        // All-Allies bucket is left UNMATCHED so the caller can sum it across the team and match it to each recipient's
        // own cast — mirroring how SumAttackBoostPercents' ally portion is summed team-wide.
        private static AbilityPotencyBreakdown ClassifyAbilityPotency(Dictionary<string, int>? passivePoints)
        {
            var breakdown = new AbilityPotencyBreakdown();
            if (passivePoints == null)
            {
                return breakdown;
            }

            foreach (var (scoringLabel, points) in passivePoints)
            {
                if (points <= 0)
                {
                    continue;
                }

                var label = FormatPassiveDisplayLabel(scoringLabel);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                // Only ability-potency / ability-damage / elemental-potency families feed the damage potency layer.
                var isAbilityPotFamily = label.Contains("Ability Pot", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Ability Dmg", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Ability Damage", StringComparison.OrdinalIgnoreCase)
                    || IsElementAbilityPassiveLabel(label);
                if (!isAbilityPotFamily || !TryGetPassiveBonusValue(scoringLabel, points, out var pct) || pct == 0)
                {
                    continue;
                }

                // "(All Allies)" passives buff the whole team → team pool; everything else is the character's own.
                var target = label.Contains("All Allies", StringComparison.OrdinalIgnoreCase)
                    ? breakdown.AllAllies
                    : breakdown.Self;

                var element = ExtractPotencyElementName(label);
                if (element != null)
                {
                    target.ByElement[element] = (target.ByElement.TryGetValue(element, out var existing) ? existing : 0) + pct;
                }
                else if (label.Contains("Phys.", StringComparison.OrdinalIgnoreCase))
                {
                    target.Phys += pct;
                }
                else if (label.Contains("Mag.", StringComparison.OrdinalIgnoreCase))
                {
                    target.Mag += pct;
                }
                else
                {
                    target.General += pct; // generic Boost Ability Pot. / Arcanum / Mastery ability dmg
                }
            }

            return breakdown;
        }

        // Maps the team's detected active buffs/debuffs onto the DamageCalc request (the "assume team's detected
        // buffs active" choice). Axis-gated to the attacker (phys/mag) and element-gated to the cast for elemental
        // effects. Same-family effects share a battle cap, so the strongest wins (Max). Resolved-fraction slots
        // bypass the tier-string lookups; the *Tier/raw double slots take a percent (PercentInputToDecimal inside).
        private static void ApplyTeamEffectsToRequest(
            DamageCalcRequest req,
            IReadOnlyList<InteractiveTeamEffect> buffs,
            IReadOnlyList<InteractiveTeamEffect> debuffs,
            bool physApplies, bool magApplies, string castElement)
        {
            var elementalCast = ContainsElementName(castElement);
            bool ElementMatches(InteractiveTeamEffect e) =>
                elementalCast && e.DisplayName != null && e.DisplayName.Contains(castElement, StringComparison.OrdinalIgnoreCase);

            foreach (var e in buffs.Concat(debuffs))
            {
                var pct = e.Potency ?? 0;       // percent (e.g. 20)
                var frac = pct / 100d;          // fraction (e.g. 0.20)
                if (pct <= 0)
                {
                    continue;
                }

                var physMag = e.Axis == "physical" ? physApplies : e.Axis == "magical" ? magApplies : false;

                switch (e.Family)
                {
                    case "attack_buff": // PATK/MATK Up
                        if (e.Axis == "physical" && physApplies) { req.PhysicalAttackBuffResolved = Math.Max(req.PhysicalAttackBuffResolved ?? 0, frac); }
                        else if (e.Axis == "magical" && magApplies) { req.MagicalAttackBuffResolved = Math.Max(req.MagicalAttackBuffResolved ?? 0, frac); }
                        break;
                    case "defense_debuff": // PDEF/MDEF Down — shrinks the enemy-defense denominator
                        if (e.Axis == "physical") { req.PhysicalDefenseDebuffResolved = Math.Max(req.PhysicalDefenseDebuffResolved ?? 0, frac); }
                        else if (e.Axis == "magical") { req.MagicDefenseDebuffResolved = Math.Max(req.MagicDefenseDebuffResolved ?? 0, frac); }
                        break;
                    case "elemental_resistance_debuff": // stacks on the reference enemy's -100 weakness
                        if (ElementMatches(e)) { req.ElementalResistanceDebuffResolved = Math.Max(req.ElementalResistanceDebuffResolved ?? 0, frac); }
                        break;
                    case "weapon_boost":
                        if (e.Axis == "elemental") { if (ElementMatches(e)) { req.ElementalWeaponBuffTier = Math.Max(req.ElementalWeaponBuffTier, pct); } }
                        else if (physMag) { req.PhysMagWeaponBuffTier = Math.Max(req.PhysMagWeaponBuffTier, pct); }
                        break;
                    case "damage_up": // Elem. Pot. Up buff
                        if (ElementMatches(e)) { req.ElementalPotUpBuffResolved = Math.Max(req.ElementalPotUpBuffResolved ?? 0, frac); }
                        break;
                    case "ability_amplification":
                        if (e.Axis == "elemental") { if (ElementMatches(e)) { req.ElementalAmplification = Math.Max(req.ElementalAmplification, pct); } }
                        else if (physMag) { req.AmplificationPhysicalAbility = Math.Max(req.AmplificationPhysicalAbility, pct); }
                        break;
                    case "damage_bonus":
                        if (e.Axis == "elemental") { if (ElementMatches(e)) { req.ElementalBonusAdditionalDamage = Math.Max(req.ElementalBonusAdditionalDamage, pct); } }
                        else if (physMag) { req.PhysMagBonusAdditionalDamage = Math.Max(req.PhysMagBonusAdditionalDamage, pct); }
                        break;
                    case "damage_received_up":
                        if (e.Axis == "elemental") { if (ElementMatches(e)) { req.ElementalDamageReceivedUp = Math.Max(req.ElementalDamageReceivedUp, pct); } }
                        else if (physMag) { req.PhysMagDamageReceivedUp = Math.Max(req.PhysMagDamageReceivedUp, pct); }
                        break;
                }
            }
        }

        // ===== Character Stats (Phase 2): true total attack stat from player-entered base/stream stats + Highwind =====
        private static readonly JsonSerializerOptions CharacterStatsJsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static CharacterStatsContext? ParseCharacterStats(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var blob = JsonSerializer.Deserialize<CharacterStatsBlob>(json, CharacterStatsJsonOptions);
                return blob?.Characters is { Count: > 0 } ? new CharacterStatsContext(blob) : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class CharacterStatsBlob
        {
            public Dictionary<string, double>? Highwind { get; set; }
            public Dictionary<string, CharacterStatsEntry>? Characters { get; set; }
        }

        private sealed class CharacterStatsEntry
        {
            public Dictionary<string, double>? Base { get; set; }
            public Dictionary<string, double>? CharacterStream { get; set; }
            public Dictionary<string, double>? RoleStream { get; set; }
        }

        private sealed class CharacterStatsContext
        {
            private readonly CharacterStatsBlob _blob;
            private readonly Dictionary<string, CharacterStatsEntry> _byName;

            public CharacterStatsContext(CharacterStatsBlob blob)
            {
                _blob = blob;
                _byName = new Dictionary<string, CharacterStatsEntry>(blob.Characters!, StringComparer.OrdinalIgnoreCase);
            }

            private static double Val(Dictionary<string, double>? row, string statKey)
                => row != null && row.TryGetValue(statKey, out var v) ? v : 0d;

            // Account-wide Highwind bonus line by key (stat keys hp/patk/... plus ability-potency lines like
            // "wpnCAbilityPot"). Returns the raw percent (e.g. 31 for +31%), 0 when unset.
            public double GetHighwind(string key) => Val(_blob.Highwind, key);

            public bool TryGetIntrinsic(string characterName, string statKey, out double intrinsic)
            {
                intrinsic = 0;
                if (!_byName.TryGetValue(characterName, out var entry))
                {
                    return false;
                }

                intrinsic = Val(entry.Base, statKey) + Val(entry.CharacterStream, statKey) + Val(entry.RoleStream, statKey);
                return true;
            }

            // floor((base + characterStream + roleStream + weapon total) × (1 + Highwind%) × passiveMultiplier).
            // passiveMultiplier folds in the always-on Boost-PATK/MATK/ATK R-abilities. Branding is not recorded.
            public int? ComputeAttackStat(string characterName, string statKey, double weaponTotal, double passiveMultiplier)
            {
                if (!TryGetIntrinsic(characterName, statKey, out var intrinsic))
                {
                    return null;
                }

                var highwind = Val(_blob.Highwind, statKey) / 100d;
                return (int)Math.Floor((intrinsic + weaponTotal) * (1 + highwind) * passiveMultiplier);
            }
        }
    }
}
