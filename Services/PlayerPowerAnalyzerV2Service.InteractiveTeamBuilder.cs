using System.Collections.Concurrent;
using System.Globalization;
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
        public InteractiveTeamScoreResult ScoreFixedTeam(string localInventoryStateJson, InteractiveTeamSpec spec)
        {
            var result = new InteractiveTeamScoreResult();
            spec ??= new InteractiveTeamSpec();

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

            // Per-character output, ordered the same way the spec listed them.
            var outputsByName = teamCandidate.Characters.ToDictionary(c => c.CharacterName, StringComparer.OrdinalIgnoreCase);
            foreach (var charSpec in characterSpecs)
            {
                if (!outputsByName.TryGetValue(charSpec.Character, out var built))
                {
                    continue;
                }

                passivePointsByCharacter.TryGetValue(built.CharacterName, out var characterPassivePoints);

                result.Characters.Add(new InteractiveTeamCharacterResult
                {
                    Name = built.CharacterName,
                    Role = built.Role.ToString(),
                    Patk = built.TotalPatk,
                    Matk = built.TotalMatk,
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
    }
}
