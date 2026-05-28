using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class PlayerPowerAnalyzerV2Service
    {
        private static readonly JsonSerializerOptions InventoryJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Dictionary<string, string> CharacterPortraits = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Aerith"] = "Aerith.jpg",
            ["Angeal"] = "Angeal.jpg",
            ["Barret"] = "Barret.jpg",
            ["Cait Sith"] = "Cait Sith.jpg",
            ["Cid"] = "Cid.jpg",
            ["Cloud"] = "Cloud.jpg",
            ["Glenn"] = "Glenn.jpg",
            ["Lucia"] = "Lucia.jpg",
            ["Matt"] = "Matt.jpg",
            ["Red XIII"] = "Red XIII.jpg",
            ["Sephiroth"] = "Sephiroth.jpg",
            ["Sephiroth (Original)"] = "Sephiroth (Original).jpg",
            ["Tifa"] = "Tifa.jpg",
            ["Vincent"] = "Vincent.jpg",
            ["Yuffie"] = "Yuffie.jpg",
            ["Zack"] = "Zack.jpg"
        };

        private static readonly int[] StandardBreakpointPoints = [1, 5, 15, 25, 35, 45, 55];
        private static readonly double[] BoostPatkAndMatkBonuses = [5, 10, 15, 20, 30, 40, 50];
        private static readonly double[] BoostAbilityPotBonuses = [3, 8, 15, 22, 30, 35, 40];
        private static readonly double[] ElementPotBonuses = [6, 15, 25, 40, 55, 70, 85, 100, 110, 120];
        private static readonly int[] ElementPotBreakpointPoints = [1, 5, 15, 25, 35, 45, 55, 65, 80, 100];
        private static readonly double[] BoostPhysAndMagAbilityPotBonuses = [5, 15, 30, 45, 60, 70, 80];
        private static readonly double[] ElementAbilityAllAlliesBonuses = [5, 10, 15];
        private static readonly int[] ElementAbilityAllAlliesBreakpointPoints = [1, 5, 15];
        private static readonly double[] BoostPatkAndMatkAllAlliesBonuses = [5, 10, 14, 18, 22, 25, 28];
        private static readonly double[] BoostAtkAllAlliesBonuses = [3, 5, 7, 9, 11, 13, 14];
        private static readonly double[] BoostAtkBonuses = [3, 5, 7, 10, 15, 20, 25];
        private static readonly double[] BoostPdefAndMdefAllAlliesBonuses = [5, 10, 20, 30, 40];
        private static readonly int[] BoostPdefAndMdefAllAlliesBreakpointPoints = [1, 5, 15, 25, 35];

        private readonly WeaponSearchDataService _weaponSearchDataService;
        private readonly TeamTemplateCatalog _teamTemplateCatalog;
        private readonly MaxDamageReferenceCatalog _maxDamageReferenceCatalog;

        public PlayerPowerAnalyzerV2Service(WeaponSearchDataService weaponSearchDataService, TeamTemplateCatalog teamTemplateCatalog, MaxDamageReferenceCatalog maxDamageReferenceCatalog)
        {
            _weaponSearchDataService = weaponSearchDataService;
            _teamTemplateCatalog = teamTemplateCatalog;
            _maxDamageReferenceCatalog = maxDamageReferenceCatalog;
        }

        public static IReadOnlyList<PlayerPowerAnalyzerV2EffectOption> AvailableEffectOptions { get; } = new List<PlayerPowerAnalyzerV2EffectOption>
        {
            new() { Key = "elemental_resistance_down", Label = "Elemental Resistance Down", Group = "Elemental Setup" },
            new() { Key = "elemental_damage_up", Label = "Elemental Damage Up", Group = "Elemental Setup" },
            new() { Key = "elemental_damage_received_up", Label = "Elemental Damage Received Up", Group = "Elemental Setup" },
            new() { Key = "elemental_damage_bonus", Label = "Elemental Damage Bonus", Group = "Elemental Setup" },
            new() { Key = "elemental_weapon_boost", Label = "Elemental Weapon Boost", Group = "Elemental Setup" },
            new() { Key = "patk_up", Label = "PATK Up", Group = "Buffs / Debuffs" },
            new() { Key = "matk_up", Label = "MATK Up", Group = "Buffs / Debuffs" },
            new() { Key = "pdef_up", Label = "PDEF Up", Group = "Buffs / Debuffs" },
            new() { Key = "mdef_up", Label = "MDEF Up", Group = "Buffs / Debuffs" },
            new() { Key = "patk_down", Label = "PATK Down", Group = "Buffs / Debuffs" },
            new() { Key = "matk_down", Label = "MATK Down", Group = "Buffs / Debuffs" },
            new() { Key = "pdef_down", Label = "PDEF Down", Group = "Buffs / Debuffs" },
            new() { Key = "mdef_down", Label = "MDEF Down", Group = "Buffs / Debuffs" },
            new() { Key = "stat_debuff_tier_increase", Label = "Applied Stats Debuff Tier Increased", Group = "Buffs / Debuffs" },
            new() { Key = "haste", Label = "Haste", Group = "Status / Utility" },
            new() { Key = "exploit_weakness", Label = "Exploit Weakness", Group = "Status / Utility" },
            new() { Key = "enfeeble", Label = "Enfeeble", Group = "Status / Utility" },
            new() { Key = "enliven", Label = "Enliven", Group = "Status / Utility" },
            new() { Key = "torpor", Label = "Torpor", Group = "Status / Utility" },
            new() { Key = "healing_support", Label = "Healing Support", Group = "Status / Utility" }
        };

        public static IReadOnlyList<PlayerPowerAnalyzerV2EffectOption> AvailableBossImmunityOptions { get; } = new List<PlayerPowerAnalyzerV2EffectOption>
        {
            new() { Key = "patk_down", Label = "PATK Down Immunity", Group = "Stat Debuff Immunities" },
            new() { Key = "matk_down", Label = "MATK Down Immunity", Group = "Stat Debuff Immunities" },
            new() { Key = "pdef_down", Label = "PDEF Down Immunity", Group = "Stat Debuff Immunities" },
            new() { Key = "mdef_down", Label = "MDEF Down Immunity", Group = "Stat Debuff Immunities" },
            new() { Key = "elemental_resistance_down", Label = "Elemental Resistance Down Immunity", Group = "Elemental Setup Immunities" },
            new() { Key = "elemental_damage_received_up", Label = "Elemental Damage Received Up Immunity", Group = "Elemental Setup Immunities" },
            new() { Key = "exploit_weakness", Label = "Exploit Weakness Immunity", Group = "Status / Ailment Immunities" },
            new() { Key = "enfeeble", Label = "Enfeeble Immunity", Group = "Status / Ailment Immunities" },
            new() { Key = "enliven", Label = "Enliven Immunity", Group = "Status / Ailment Immunities" },
            new() { Key = "torpor", Label = "Torpor Immunity", Group = "Status / Ailment Immunities" },
            new() { Key = "stat_debuffs", Label = "All Stat Debuff Immunities", Group = "Legacy Broad Immunities" },
            new() { Key = "status_ailments", Label = "All Status / Ailment Immunities", Group = "Legacy Broad Immunities" },
            new() { Key = "elemental_setup", Label = "All Elemental Setup Immunities", Group = "Legacy Broad Immunities" }
        };

        public PlayerPowerAnalyzerV2Result Analyze(string localInventoryStateJson, PlayerPowerAnalyzerV2Request request)
        {
            var result = new PlayerPowerAnalyzerV2Result();
            var inventoryState = ParseInventoryState(localInventoryStateJson, result);
            if (inventoryState == null)
            {
                return result;
            }

            var enabledTemplateNames = request.EnabledTeamTemplates.Count > 0
                ? request.EnabledTeamTemplates
                : _teamTemplateCatalog.GetEnabledTemplates().Select(t => t.Name).ToList();
            var normalizedEnabledTemplateNames = enabledTemplateNames
                .GroupBy(NormalizeTemplateName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var mutuallyExclusiveCharacterGroups = _teamTemplateCatalog.GetMutuallyExclusiveCharacterGroups()
                .Select(group => new HashSet<string>(group, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var referenceTuningProfile = BuildReferenceTuningProfile(request);
            var weaponSlotEvaluationCache = new Dictionary<string, SlotEvaluation>(StringComparer.OrdinalIgnoreCase);
            var costumeSlotEvaluationCache = new Dictionary<string, SlotEvaluation>(StringComparer.OrdinalIgnoreCase);

            var ownedWeapons = new List<OwnedWeaponCandidate>();
            var ownedCostumesByCharacter = new Dictionary<string, List<OwnedCostumeCandidate>>(StringComparer.OrdinalIgnoreCase);
            var allItems = _weaponSearchDataService.GetWeapons().ToList();
            var availableCharacters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unsetLevelCount = 0;

            foreach (var item in allItems)
            {
                if (string.Equals(item.EquipmentType, "Costume", StringComparison.OrdinalIgnoreCase))
                {
                    if (inventoryState.Costumes.TryGetValue(item.Id, out var costumeState) && costumeState?.Owned == true)
                    {
                        if (!ownedCostumesByCharacter.TryGetValue(item.Character, out var list))
                        {
                            list = new List<OwnedCostumeCandidate>();
                            ownedCostumesByCharacter[item.Character] = list;
                        }

                        list.Add(new OwnedCostumeCandidate(item));
                        availableCharacters.Add(item.Character);
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
                if (!weaponState?.Level.HasValue ?? true)
                {
                    unsetLevelCount++;
                }

                var snapshot = _weaponSearchDataService.GetWeaponSnapshot(item.Id, overboostLevel.Value, level);
                if (snapshot == null)
                {
                    continue;
                }

                ownedWeapons.Add(new OwnedWeaponCandidate(item, snapshot, overboostLevel.Value, level));
                availableCharacters.Add(item.Character);
            }

            result.AvailableCharacterCount = availableCharacters.Count;
            result.UnsetLevelWeaponCount = unsetLevelCount;
            if (unsetLevelCount > 0)
            {
                result.Warnings.Add($"{unsetLevelCount} owned weapon{(unsetLevelCount == 1 ? string.Empty : "s")} had no saved inventory level and were evaluated at Level {_weaponSearchDataService.MaxWeaponLevel}.");
            }

            var ownedWeaponsByCharacter = ownedWeapons
                .Where(w => !w.IsUltimate)
                .GroupBy(w => w.Character, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var ultimateWeaponsByCharacter = ownedWeapons
                .Where(w => w.IsUltimate)
                .GroupBy(w => w.Character, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var charactersWithMainWeapon = ownedWeaponsByCharacter.Keys.ToList();

            if (charactersWithMainWeapon.Count < 3)
            {
                result.IsPlaceholder = true;
                result.FailureReason = $"At least 3 characters with owned non-ultimate weapons are needed for V2 analysis. Current local inventory coverage: {charactersWithMainWeapon.Count}.";
                return result;
            }

            var variantsByCharacter = charactersWithMainWeapon.ToDictionary(
                character => character,
                character => BuildCharacterVariants(character, ownedWeaponsByCharacter, ultimateWeaponsByCharacter, ownedCostumesByCharacter, request, referenceTuningProfile, weaponSlotEvaluationCache, costumeSlotEvaluationCache),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in variantsByCharacter.Where(e => e.Value.Count == 0))
            {
                result.DebugNotes.Add($"{entry.Key}: no valid base build variants were generated.");
            }

            var teamCandidates = BuildTeamCandidates(variantsByCharacter, ownedWeapons, request, referenceTuningProfile, normalizedEnabledTemplateNames, mutuallyExclusiveCharacterGroups, weaponSlotEvaluationCache).ToList();
            if (teamCandidates.Count == 0)
            {
                result.FailureReason = request.HardRequiredEffectKeys.Count > 0
                    ? "No valid V2 team matched the selected hard-required effects with the current local inventory."
                    : "No valid V2 team could be assembled from the current local inventory.";
                return result;
            }

            var best = teamCandidates.OrderByDescending(t => t.Score).First();
            result.HasResult = true;
            result.Score = Math.Round(best.Score, 2);
            result.TeamCharacters = best.Characters.Select(c => c.CharacterName).ToList();
            result.Characters = best.Characters;
            result.MatchedTemplateName = best.TemplateName;
            result.MatchedRequiredEffects = best.MatchedRequiredEffects.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            result.MissingRequiredEffects = best.MissingRequiredEffects.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            result.MatchedPreferredEffects = best.MatchedPreferredEffects.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            result.MissingPreferredEffects = best.MissingPreferredEffects.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            result.ProvidedEffectLabels = best.ProvidedEffectKeys.Select(ToLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            result.SuppressedEffectNotes = best.SuppressedEffectNotes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.DebugNotes = best.DebugNotes.ToList();
            result.AlternateTeams = teamCandidates
                .Where(t => !string.Equals(t.TeamKey, best.TeamKey, StringComparison.OrdinalIgnoreCase))
                .GroupBy(t => t.TeamKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(t => t.Score)
                .Take(5)
                .Select(t => new PlayerPowerAnalyzerV2AlternateTeam
                {
                    Characters = t.Characters.Select(c => c.CharacterName).ToList(),
                    Score = Math.Round(t.Score, 2)
                })
                .ToList();
            return result;
        }

        private static LocalInventoryState? ParseInventoryState(string localInventoryStateJson, PlayerPowerAnalyzerV2Result result)
        {
            if (string.IsNullOrWhiteSpace(localInventoryStateJson))
            {
                result.FailureReason = "No Player Inventory data was supplied. Update or import inventory on the Player Inventory page, then try again.";
                return null;
            }

            try
            {
                var inventoryState = JsonSerializer.Deserialize<LocalInventoryState>(localInventoryStateJson, InventoryJsonOptions);
                if (inventoryState == null)
                {
                    result.FailureReason = "The supplied Player Inventory data was empty.";
                }

                return inventoryState;
            }
            catch (JsonException)
            {
                result.FailureReason = "The supplied Player Inventory data could not be read.";
                return null;
            }
        }

        private List<CharacterBuildCandidate> BuildCharacterVariants(
            string character,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ownedWeaponsByCharacter,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ultimateWeaponsByCharacter,
            Dictionary<string, List<OwnedCostumeCandidate>> ownedCostumesByCharacter,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            Dictionary<string, SlotEvaluation> weaponSlotEvaluationCache,
            Dictionary<string, SlotEvaluation> costumeSlotEvaluationCache)
        {
            var role = CharacterRoleRegistry.GetRoleOrDefault(character);
            var mainWeapons = ownedWeaponsByCharacter.TryGetValue(character, out var characterWeapons)
                ? characterWeapons
                : new List<OwnedWeaponCandidate>();
            if (mainWeapons.Count == 0)
            {
                return new List<CharacterBuildCandidate>();
            }

            var ultimates = ultimateWeaponsByCharacter.TryGetValue(character, out var characterUltimates)
                ? characterUltimates
                : new List<OwnedWeaponCandidate>();
            ownedCostumesByCharacter.TryGetValue(character, out var costumes);
            var mainOptions = mainWeapons
                .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Main Weapon", 1.0, true, true, weaponSlotEvaluationCache))
                .OrderByDescending(x => x.Score)
                .Take(4)
                .ToList();
            var ultimateOptions = ultimates
                .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Ultimate", 1.0, true, true, weaponSlotEvaluationCache))
                .OrderByDescending(x => x.Score)
                .Take(2)
                .ToList();
            var costumeOptions = (costumes ?? new List<OwnedCostumeCandidate>())
                .Select(c => GetOrCreateCostumeSlot(c, role, request, referenceTuningProfile, "Main Outfit", 1.0, true, costumeSlotEvaluationCache))
                .OrderByDescending(x => x.Score)
                .Take(3)
                .ToList();

            var variants = new List<CharacterBuildCandidate>();
            foreach (var main in mainOptions)
            {
                var offOptions = mainWeapons
                    .Where(w => !w.Item.Name.Equals(main.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Off-hand", 0.5, true, true, weaponSlotEvaluationCache))
                    .OrderByDescending(x => x.Score)
                    .Take(3)
                    .Cast<SlotEvaluation?>()
                    .ToList();
                if (offOptions.Count == 0)
                {
                    offOptions.Add(null);
                }

                var mainCostumeChoices = costumeOptions
                    .Cast<SlotEvaluation?>()
                    .ToList();
                if (mainCostumeChoices.Count == 0)
                {
                    mainCostumeChoices.Add(null);
                }
                foreach (var off in offOptions)
                {
                    var ultChoices = ultimateOptions.Count > 0 ? ultimateOptions.Cast<SlotEvaluation?>().ToList() : new List<SlotEvaluation?> { null };
                    foreach (var ultimate in ultChoices)
                    {
                        foreach (var mainCostume in mainCostumeChoices)
                        {
                            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { main.Name };
                            if (off != null)
                            {
                                usedNames.Add(off.Name);
                            }

                            if (ultimate != null)
                            {
                                usedNames.Add(ultimate.Name);
                            }

                            var subOutfits = new List<PlayerPowerAnalyzerV2ItemSlot>();
                            var subOutfitPassivePoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            var subOutfitScore = 0d;
                            if (mainCostume != null && costumes != null)
                            {
                                usedNames.Add(mainCostume.Name);
                                var remainingCostumes = costumes
                                    .Where(c => !c.Item.Name.Equals(mainCostume.Name, StringComparison.OrdinalIgnoreCase))
                                    .Select(c => GetOrCreateCostumeSlot(c, role, request, referenceTuningProfile, "Sub Outfit", 0.5, false, costumeSlotEvaluationCache))
                                    .OrderByDescending(x => x.Score)
                                    .Take(2)
                                    .ToList();
                                foreach (var sub in remainingCostumes)
                                {
                                    if (!usedNames.Add(sub.Name))
                                    {
                                        continue;
                                    }

                                    subOutfits.Add(sub.Slot);
                                    subOutfitScore += sub.NonPassiveScore;
                                    AddPassivePoints(subOutfitPassivePoints, sub.PassivePoints);
                                }
                            }

                            var passivePoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            AddPassivePoints(passivePoints, main.PassivePoints);
                            if (off != null)
                            {
                                AddPassivePoints(passivePoints, off.PassivePoints);
                            }

                            if (ultimate != null)
                            {
                                AddPassivePoints(passivePoints, ultimate.PassivePoints);
                            }

                            if (mainCostume != null)
                            {
                                AddPassivePoints(passivePoints, mainCostume.PassivePoints);
                            }

                            AddPassivePoints(passivePoints, subOutfitPassivePoints);
                            var nonPassiveScore = main.NonPassiveScore + (off?.NonPassiveScore ?? 0) + (ultimate?.NonPassiveScore ?? 0) + (mainCostume?.NonPassiveScore ?? 0) + subOutfitScore;
                            var characterScore = nonPassiveScore + ScorePassivePoints(passivePoints, role, request);
                            var providedKeys = new HashSet<string>(main.ProvidedEffectKeys, StringComparer.OrdinalIgnoreCase);
                            if (off != null)
                            {
                                foreach (var key in off.ProvidedEffectKeys)
                                {
                                    providedKeys.Add(key);
                                }
                            }

                            if (ultimate != null)
                            {
                                foreach (var key in ultimate.ProvidedEffectKeys)
                                {
                                    providedKeys.Add(key);
                                }
                            }

                            if (mainCostume != null)
                            {
                                foreach (var key in mainCostume.ProvidedEffectKeys)
                                {
                                    providedKeys.Add(key);
                                }
                            }

                            variants.Add(new CharacterBuildCandidate
                            {
                                CharacterName = character,
                                Role = role,
                                BaseScore = characterScore,
                                NonPassiveScore = nonPassiveScore,
                                MainWeapon = main.Slot,
                                MainPassivePoints = new Dictionary<string, int>(main.PassivePoints, StringComparer.OrdinalIgnoreCase),
                                OffHandWeapon = off?.Slot,
                                OffPassivePoints = off == null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, int>(off.PassivePoints, StringComparer.OrdinalIgnoreCase),
                                UltimateWeapon = ultimate?.Slot,
                                UltimatePassivePoints = ultimate == null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, int>(ultimate.PassivePoints, StringComparer.OrdinalIgnoreCase),
                                MainOutfit = mainCostume?.Slot,
                                MainOutfitPassivePoints = mainCostume == null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, int>(mainCostume.PassivePoints, StringComparer.OrdinalIgnoreCase),
                                SubOutfits = subOutfits,
                                SubOutfitPassivePoints = subOutfitPassivePoints,
                                ProvidedEffectKeys = providedKeys,
                                UsedItemNames = usedNames,
                                PassivePoints = passivePoints
                            });
                        }
                    }
                }
            }

            return variants
                .OrderByDescending(v => v.BaseScore + PreferredCoverageBonus(v.ProvidedEffectKeys, request))
                .Take(6)
                .ToList();
        }

        private IEnumerable<TeamCandidate> BuildTeamCandidates(
            Dictionary<string, List<CharacterBuildCandidate>> variantsByCharacter,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            IReadOnlyList<HashSet<string>> mutuallyExclusiveCharacterGroups,
            Dictionary<string, SlotEvaluation> weaponSlotEvaluationCache)
        {
            var characters = variantsByCharacter.Where(kvp => kvp.Value.Count > 0).Select(kvp => kvp.Key).ToList();
            for (var i = 0; i < characters.Count; i++)
            {
                for (var j = i + 1; j < characters.Count; j++)
                {
                    for (var k = j + 1; k < characters.Count; k++)
                    {
                        var teamCharacters = new[] { characters[i], characters[j], characters[k] };
                        if (!IsCharacterCombinationAllowed(teamCharacters, mutuallyExclusiveCharacterGroups))
                        {
                            continue;
                        }

                        foreach (var candidate in BuildTeamCandidatesForCharacters(teamCharacters, variantsByCharacter, ownedWeapons, request, referenceTuningProfile, normalizedEnabledTemplateNames, weaponSlotEvaluationCache))
                        {
                            yield return candidate;
                        }
                    }
                }
            }
        }

        private IEnumerable<TeamCandidate> BuildTeamCandidatesForCharacters(
            string[] teamCharacters,
            Dictionary<string, List<CharacterBuildCandidate>> variantsByCharacter,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            Dictionary<string, SlotEvaluation> weaponSlotEvaluationCache)
        {
            var charA = variantsByCharacter[teamCharacters[0]];
            var charB = variantsByCharacter[teamCharacters[1]];
            var charC = variantsByCharacter[teamCharacters[2]];
            foreach (var a in charA)
            {
                foreach (var b in charB)
                {
                    if (HasTeamConflict(a, b))
                    {
                        continue;
                    }

                    foreach (var c in charC)
                    {
                        if (HasTeamConflict(a, c) || HasTeamConflict(b, c))
                        {
                            continue;
                        }

                        var team = FinalizeTeamCandidate(new[] { a, b, c }, ownedWeapons, request, referenceTuningProfile, normalizedEnabledTemplateNames, weaponSlotEvaluationCache);
                        if (team != null)
                        {
                            yield return team;
                        }
                    }
                }
            }
        }

        private TeamCandidate? FinalizeTeamCandidate(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            Dictionary<string, SlotEvaluation> weaponSlotEvaluationCache)
        {
            var usedItemNames = new HashSet<string>(baseVariants.SelectMany(v => v.UsedItemNames), StringComparer.OrdinalIgnoreCase);
            var characterOutputs = baseVariants.Select(v => v.ToOutput()).ToList();
            var characterOutputsByName = characterOutputs.ToDictionary(c => c.CharacterName, StringComparer.OrdinalIgnoreCase);
            var baseNonPassiveScoresByCharacter = baseVariants.ToDictionary(v => v.CharacterName, v => v.NonPassiveScore, StringComparer.OrdinalIgnoreCase);
            var passivePointsByCharacter = baseVariants.ToDictionary(v => v.CharacterName, v => new Dictionary<string, int>(v.PassivePoints, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            var subWeaponNonPassiveScoreByCharacter = baseVariants.ToDictionary(v => v.CharacterName, _ => 0d, StringComparer.OrdinalIgnoreCase);
            var availableSubWeapons = ownedWeapons
                .Where(w => !w.IsUltimate && !usedItemNames.Contains(w.Item.Name))
                .ToList();
            var assignments = new List<SubWeaponAssignment>();

            foreach (var character in baseVariants)
            {
                foreach (var weapon in availableSubWeapons)
                {
                    var evaluation = GetOrCreateWeaponSlot(weapon, character.Role, request, referenceTuningProfile, "Sub Weapon", 0.5, false, false, weaponSlotEvaluationCache);
                    assignments.Add(new SubWeaponAssignment
                    {
                        CharacterName = character.CharacterName,
                        Evaluation = evaluation
                    });
                }
            }

            var assignedCounts = baseVariants.ToDictionary(v => v.CharacterName, _ => 0, StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in assignments.OrderByDescending(a => a.Evaluation.Score))
            {
                if (assignedCounts[assignment.CharacterName] >= 3)
                {
                    continue;
                }

                if (!usedItemNames.Add(assignment.Evaluation.Name))
                {
                    continue;
                }

                var character = characterOutputsByName[assignment.CharacterName];
                character.SubWeapons.Add(assignment.Evaluation.Slot);
                character.TotalPatk += assignment.Evaluation.Slot.Patk;
                character.TotalMatk += assignment.Evaluation.Slot.Matk;
                character.TotalHeal += assignment.Evaluation.Slot.Heal;
                assignedCounts[assignment.CharacterName]++;
                subWeaponNonPassiveScoreByCharacter[assignment.CharacterName] += assignment.Evaluation.NonPassiveScore;
                AddPassivePoints(passivePointsByCharacter[assignment.CharacterName], assignment.Evaluation.PassivePoints);
            }

            var teamWidePassivePoints = AggregateTeamWidePassivePoints(passivePointsByCharacter.Values);

            foreach (var character in characterOutputs)
            {
                character.Score = Math.Round(
                    baseNonPassiveScoresByCharacter[character.CharacterName]
                    + subWeaponNonPassiveScoreByCharacter[character.CharacterName]
                    + ScoreCharacterPassivePoints(passivePointsByCharacter[character.CharacterName], teamWidePassivePoints, character.Role, request),
                    2);
                character.KeyRAbilities = passivePointsByCharacter[character.CharacterName]
                    .Where(kvp => kvp.Value > 0)
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(6)
                    .Select(kvp => $"{kvp.Key} +{kvp.Value} pts")
                    .ToList();
            }

            var providedEffectKeys = new HashSet<string>(baseVariants.SelectMany(v => v.ProvidedEffectKeys), StringComparer.OrdinalIgnoreCase);
            var missingRequired = request.HardRequiredEffectKeys
                .Where(key => !providedEffectKeys.Contains(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missingRequired.Count > 0)
            {
                return null;
            }

            var matchedPreferred = request.SoftPreferredEffectKeys
                .Where(key => providedEffectKeys.Contains(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var missingPreferred = request.SoftPreferredEffectKeys
                .Where(key => !providedEffectKeys.Contains(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var score = characterOutputs.Sum(c => c.Score);
            score += ScoreTeamEffects(providedEffectKeys, request);
            score += matchedPreferred.Count * 90;
            score += request.HardRequiredEffectKeys.Count * 40;
            var referencePatternBonus = ScoreReferencePatternSynergyBonus(baseVariants, providedEffectKeys, request, referenceTuningProfile);
            score += referencePatternBonus;

            var roles = characterOutputs.Select(c => c.Role.ToString()).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
            var teamRoleKey = NormalizeTemplateName(string.Join("/", roles));
            normalizedEnabledTemplateNames.TryGetValue(teamRoleKey, out var matchedTemplateName);
            if (string.IsNullOrWhiteSpace(matchedTemplateName))
            {
                score *= 0.5;
            }

            var debugNotes = new List<string>
            {
                $"Roles: {string.Join(", ", roles)}",
                string.IsNullOrWhiteSpace(matchedTemplateName) ? "No enabled team template matched (50% penalty applied)." : $"Matched team template: {matchedTemplateName}"
            };
            if (referencePatternBonus > 0)
            {
                debugNotes.Add($"Reference-informed tuning bonus applied: +{referencePatternBonus:0.##}.");
            }

            AppendReferenceMatchDebugNotes(debugNotes, request, characterOutputs, providedEffectKeys);

            return new TeamCandidate
            {
                Characters = characterOutputs.OrderByDescending(c => c.Score).ToList(),
                Score = score,
                TemplateName = matchedTemplateName,
                MatchedRequiredEffects = request.HardRequiredEffectKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                MissingRequiredEffects = missingRequired,
                MatchedPreferredEffects = matchedPreferred,
                MissingPreferredEffects = missingPreferred,
                ProvidedEffectKeys = providedEffectKeys.ToList(),
                SuppressedEffectNotes = BuildSuppressedEffectNotes(request.BossImmunityKeys),
                DebugNotes = debugNotes,
                TeamKey = string.Join("|", characterOutputs.Select(c => c.CharacterName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            };
        }

        private void AppendReferenceMatchDebugNotes(
            List<string> debugNotes,
            PlayerPowerAnalyzerV2Request request,
            IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characterOutputs,
            IEnumerable<string> providedEffectKeys)
        {
            var match = _maxDamageReferenceCatalog.FindClosestMatch(request.EnemyWeakness, request.PreferredDamageType, characterOutputs, providedEffectKeys);
            if (match == null)
            {
                return;
            }

            debugNotes.Add($"Closest max-damage reference archetype: {match.ArchetypeId} (score {match.Score:0.##}).");

            if (match.MatchingSignals.Count > 0)
            {
                debugNotes.Add($"Reference overlap: {string.Join("; ", match.MatchingSignals)}");
            }

            if (match.MissingSignals.Count > 0)
            {
                debugNotes.Add($"Reference gaps: {string.Join("; ", match.MissingSignals.Take(3))}");
            }

            if (match.ReferenceProfileNotes.Count > 0)
            {
                debugNotes.Add($"Reference profile: {string.Join("; ", match.ReferenceProfileNotes)}");
            }
        }

        private SlotEvaluation GetOrCreateWeaponSlot(
            OwnedWeaponCandidate weapon,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            string slotName,
            double slotMultiplier,
            bool includeActiveEffects,
            bool includeDamage,
            Dictionary<string, SlotEvaluation> cache)
        {
            var cacheKey = string.Join("|", weapon.Item.Id, role, slotName, slotMultiplier, includeActiveEffects, includeDamage);
            if (!cache.TryGetValue(cacheKey, out var evaluation))
            {
                evaluation = CreateWeaponSlot(weapon, role, request, referenceTuningProfile, slotName, slotMultiplier, includeActiveEffects, includeDamage);
                cache[cacheKey] = evaluation;
            }

            return evaluation;
        }

        private SlotEvaluation GetOrCreateCostumeSlot(
            OwnedCostumeCandidate costume,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            string slotName,
            double slotMultiplier,
            bool includeActiveEffects,
            Dictionary<string, SlotEvaluation> cache)
        {
            var cacheKey = string.Join("|", costume.Item.Id, role, slotName, slotMultiplier, includeActiveEffects);
            if (!cache.TryGetValue(cacheKey, out var evaluation))
            {
                evaluation = CreateCostumeSlot(costume, role, request, referenceTuningProfile, slotName, slotMultiplier, includeActiveEffects);
                cache[cacheKey] = evaluation;
            }

            return evaluation;
        }

        private static string NormalizeTemplateName(string templateName)
        {
            return string.Join("/", (templateName ?? string.Empty)
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase));
        }

        private static bool IsCharacterCombinationAllowed(
            IReadOnlyCollection<string> teamCharacters,
            IReadOnlyList<HashSet<string>> mutuallyExclusiveCharacterGroups)
        {
            if (mutuallyExclusiveCharacterGroups.Count == 0)
            {
                return true;
            }

            var set = new HashSet<string>(teamCharacters, StringComparer.OrdinalIgnoreCase);
            foreach (var group in mutuallyExclusiveCharacterGroups)
            {
                if (group.Count(character => set.Contains(character)) > 1)
                {
                    return false;
                }
            }

            return true;
        }

        private SlotEvaluation CreateWeaponSlot(OwnedWeaponCandidate weapon, CharacterRole role, PlayerPowerAnalyzerV2Request request, ReferenceTuningProfile referenceTuningProfile, string slotName, double slotMultiplier, bool includeActiveEffects, bool includeDamage)
        {
            var passivePoints = BuildPassivePointMap(weapon.Snapshot.RAbilities, slotMultiplier);
            var passiveSummaries = passivePoints
                .Where(kvp => kvp.Value > 0)
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => $"{kvp.Key} +{kvp.Value} pts")
                .ToList();
            var selectedCustomization = SelectBestCustomization(weapon, role, request, referenceTuningProfile, slotMultiplier, includeActiveEffects);
            if (selectedCustomization.PassiveSkillName != null && selectedCustomization.PassiveSkillPoints > 0)
            {
                var appliedPoints = Math.Max(0, (int)Math.Floor(selectedCustomization.PassiveSkillPoints * slotMultiplier));
                if (appliedPoints > 0)
                {
                    passivePoints[selectedCustomization.PassiveSkillName] = passivePoints.TryGetValue(selectedCustomization.PassiveSkillName, out var existing)
                        ? existing + appliedPoints
                        : appliedPoints;
                    passiveSummaries.Insert(0, $"{selectedCustomization.PassiveSkillName} +{appliedPoints} pts");
                }
            }

            var patk = Math.Max(0, (int)Math.Floor(weapon.Snapshot.Patk * slotMultiplier));
            var matk = Math.Max(0, (int)Math.Floor(weapon.Snapshot.Matk * slotMultiplier));
            var heal = Math.Max(0, (int)Math.Floor(weapon.Snapshot.Heal * slotMultiplier));
            var statScore = ScoreStats(patk, matk, heal, role, request);
            var passiveScore = ScorePassivePoints(passivePoints, role, request);
            var nonPassiveScore = statScore;
            var score = nonPassiveScore + passiveScore;
            var providedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (includeDamage)
            {
                var damageScore = ScoreDamageWithReferenceTuning(weapon, role, request, referenceTuningProfile);
                nonPassiveScore += damageScore;
                score += damageScore;
            }

            if (includeActiveEffects)
            {
                foreach (var key in DetectEffectKeys(weapon.Item.EffectTags, weapon.Snapshot.AbilityText, request, request.BossImmunityKeys))
                {
                    providedKeys.Add(key);
                    var effectScore = ScoreEffectKeyWithReferenceTuning(key, role, request, false, referenceTuningProfile);
                    nonPassiveScore += effectScore;
                    score += effectScore;
                }

                if (!string.IsNullOrWhiteSpace(selectedCustomization.Description))
                {
                    foreach (var key in DetectEffectKeys(Array.Empty<string>(), selectedCustomization.Description, request, request.BossImmunityKeys))
                    {
                        providedKeys.Add(key);
                        var effectScore = ScoreEffectKeyWithReferenceTuning(key, role, request, false, referenceTuningProfile);
                        nonPassiveScore += effectScore;
                        score += effectScore;
                    }
                }
            }

            return new SlotEvaluation
            {
                Name = weapon.Item.Name,
                Slot = new PlayerPowerAnalyzerV2ItemSlot
                {
                    ItemId = weapon.Item.Id,
                    Name = weapon.Item.Name,
                    SlotName = slotName,
                    EquipmentType = weapon.Item.EquipmentType,
                    Character = weapon.Character,
                    ImageUrl = weapon.Item.ImageUrl,
                    PreviewImageUrl = weapon.Item.PreviewImageUrl,
                    Element = weapon.Item.Element,
                    AbilityType = weapon.Item.AbilityType,
                    AbilityText = weapon.Snapshot.AbilityText,
                    OverboostLevel = weapon.OverboostLevel,
                    Level = weapon.Level,
                    IsUltimate = weapon.IsUltimate,
                    SlotMultiplier = slotMultiplier,
                    Patk = patk,
                    Matk = matk,
                    Heal = heal,
                    DamagePercent = includeDamage ? weapon.Snapshot.DamagePercent : 0,
                    Score = Math.Round(score, 2),
                    SelectedCustomization = string.IsNullOrWhiteSpace(selectedCustomization.Description) ? null : selectedCustomization.Description,
                    PassiveSummaries = passiveSummaries.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList(),
                    ProvidedEffectLabels = providedKeys.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                },
                PassivePoints = passivePoints,
                ProvidedEffectKeys = providedKeys.ToList(),
                NonPassiveScore = nonPassiveScore,
                Score = score
            };
        }

        private SlotEvaluation CreateCostumeSlot(OwnedCostumeCandidate costume, CharacterRole role, PlayerPowerAnalyzerV2Request request, ReferenceTuningProfile referenceTuningProfile, string slotName, double slotMultiplier, bool includeActiveEffects)
        {
            var passivePoints = BuildPassivePointMap(costume.Item.MaxPassiveSkills, slotMultiplier);
            var nonPassiveScore = 0d;
            var score = ScorePassivePoints(passivePoints, role, request);
            var providedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (includeActiveEffects)
            {
                foreach (var key in DetectEffectKeys(costume.Item.EffectTags, costume.Item.AbilityText, request, request.BossImmunityKeys))
                {
                    providedKeys.Add(key);
                    var effectScore = ScoreEffectKeyWithReferenceTuning(key, role, request, false, referenceTuningProfile);
                    nonPassiveScore += effectScore;
                    score += effectScore;
                }
            }

            return new SlotEvaluation
            {
                Name = costume.Item.Name,
                Slot = new PlayerPowerAnalyzerV2ItemSlot
                {
                    ItemId = costume.Item.Id,
                    Name = costume.Item.Name,
                    SlotName = slotName,
                    EquipmentType = costume.Item.EquipmentType,
                    Character = costume.Item.Character,
                    ImageUrl = costume.Item.ImageUrl,
                    PreviewImageUrl = costume.Item.PreviewImageUrl,
                    Element = costume.Item.Element,
                    AbilityType = costume.Item.AbilityType,
                    AbilityText = costume.Item.AbilityText,
                    SlotMultiplier = slotMultiplier,
                    Score = Math.Round(score, 2),
                    PassiveSummaries = passivePoints.OrderByDescending(kvp => kvp.Value).Take(5).Select(kvp => $"{kvp.Key} +{kvp.Value} pts").ToList(),
                    ProvidedEffectLabels = providedKeys.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                },
                PassivePoints = passivePoints,
                ProvidedEffectKeys = providedKeys.ToList(),
                Score = score
            };
        }

        private static Dictionary<string, int> BuildPassivePointMap(IEnumerable<PassiveSkillTotal> passives, double slotMultiplier)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var passive in passives)
            {
                if (string.IsNullOrWhiteSpace(passive.SkillName))
                {
                    continue;
                }

                var appliedPoints = Math.Max(0, (int)Math.Floor(passive.TotalPoints * slotMultiplier));
                if (appliedPoints <= 0)
                {
                    continue;
                }

                map[passive.SkillName] = map.TryGetValue(passive.SkillName, out var existing)
                    ? existing + appliedPoints
                    : appliedPoints;
            }

            return map;
        }

        private static void AddPassivePoints(Dictionary<string, int> destination, Dictionary<string, int> source)
        {
            foreach (var kvp in source)
            {
                destination[kvp.Key] = destination.TryGetValue(kvp.Key, out var existing)
                    ? existing + kvp.Value
                    : kvp.Value;
            }
        }

        private static double ScoreStats(int patk, int matk, int heal, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            return role switch
            {
                CharacterRole.Healer => (heal * 0.11) + (patk * 0.02) + (matk * 0.02),
                CharacterRole.Support => (patk * 0.04) + (matk * 0.04) + (heal * 0.05),
                CharacterRole.Tank => (patk * 0.04) + (matk * 0.03) + (heal * 0.03),
                _ when request.PreferredDamageType == DamageType.Magical => (matk * 0.10) + (patk * 0.03) + (heal * 0.01),
                _ => (patk * 0.10) + (matk * 0.03) + (heal * 0.01)
            };
        }

        private static double ScorePassivePoints(Dictionary<string, int> passivePoints, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            return passivePoints.Sum(kvp => ScorePassiveSkill(kvp.Key, kvp.Value, role, request));
        }

        private static double ScoreCharacterPassivePoints(
            IReadOnlyDictionary<string, int> passivePoints,
            IReadOnlyDictionary<string, int> teamWidePassivePoints,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request)
        {
            var score = 0d;
            foreach (var kvp in passivePoints)
            {
                if (IsTeamWidePassive(kvp.Key))
                {
                    continue;
                }

                score += ScorePassiveSkill(kvp.Key, kvp.Value, role, request);
            }

            foreach (var kvp in teamWidePassivePoints)
            {
                score += ScoreTeamWidePassiveSkillForRecipient(kvp.Key, kvp.Value, role, request);
            }

            return score;
        }

        private static Dictionary<string, int> AggregateTeamWidePassivePoints(IEnumerable<Dictionary<string, int>> passivePointMaps)
        {
            var aggregate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var passivePointMap in passivePointMaps)
            {
                foreach (var kvp in passivePointMap)
                {
                    if (!IsTeamWidePassive(kvp.Key) || kvp.Value <= 0)
                    {
                        continue;
                    }

                    aggregate[kvp.Key] = aggregate.TryGetValue(kvp.Key, out var existing)
                        ? existing + kvp.Value
                        : kvp.Value;
                }
            }

            return aggregate;
        }

        private static double ScoreTeamWidePassiveSkillForRecipient(string skillName, int points, CharacterRole recipientRole, PlayerPowerAnalyzerV2Request request)
        {
            if (points <= 0 || !IsTeamWidePassive(skillName))
            {
                return 0;
            }

            var scaledValue = TryGetPassiveBonusValue(skillName, points, out var bonusValue)
                ? bonusValue
                : points;

            return scaledValue * GetTeamWidePassiveRecipientWeight(skillName, recipientRole, request);
        }

        private static bool IsTeamWidePassive(string skillName)
        {
            return !string.IsNullOrWhiteSpace(skillName)
                && skillName.Contains("All Allies", StringComparison.OrdinalIgnoreCase);
        }

        private static double GetTeamWidePassiveRecipientWeight(string skillName, CharacterRole recipientRole, PlayerPowerAnalyzerV2Request request)
        {
            var normalized = skillName.ToLowerInvariant();
            if (normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return request.PreferredDamageType == DamageType.Magical
                    ? recipientRole switch
                    {
                        CharacterRole.DPS => 0.45,
                        CharacterRole.Support => 0.3,
                        CharacterRole.Tank => 0.25,
                        CharacterRole.Healer => 0.15,
                        _ => 0.25
                    }
                    : recipientRole switch
                    {
                        CharacterRole.DPS => 3.2,
                        CharacterRole.Support => 2.0,
                        CharacterRole.Tank => 1.2,
                        CharacterRole.Healer => 0.35,
                        _ => 1.0
                    };
            }

            if (normalized.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return request.PreferredDamageType == DamageType.Magical
                    ? recipientRole switch
                    {
                        CharacterRole.DPS => 3.2,
                        CharacterRole.Support => 2.0,
                        CharacterRole.Tank => 1.2,
                        CharacterRole.Healer => 0.35,
                        _ => 1.0
                    }
                    : recipientRole switch
                    {
                        CharacterRole.DPS => 0.45,
                        CharacterRole.Support => 0.3,
                        CharacterRole.Tank => 0.25,
                        CharacterRole.Healer => 0.15,
                        _ => 0.25
                    };
            }

            if (normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return recipientRole switch
                {
                    CharacterRole.DPS => 2.6,
                    CharacterRole.Support => 1.8,
                    CharacterRole.Tank => 1.0,
                    CharacterRole.Healer => 0.3,
                    _ => 1.0
                };
            }

            if (normalized.Contains("ability (all allies)", StringComparison.OrdinalIgnoreCase)
                && request.EnemyWeakness != Element.None
                && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return recipientRole switch
                {
                    CharacterRole.DPS => 3.1,
                    CharacterRole.Support => 2.1,
                    CharacterRole.Tank => 1.1,
                    CharacterRole.Healer => 0.35,
                    _ => 1.0
                };
            }

            if (normalized.Contains("boost pdef (all allies)", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost mdef (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return recipientRole switch
                {
                    CharacterRole.Tank => 2.4,
                    CharacterRole.Support => 1.9,
                    CharacterRole.Healer => 1.8,
                    CharacterRole.DPS => 1.0,
                    _ => 1.0
                };
            }

            return GetPassiveWeight(skillName, recipientRole, request);
        }

        private static double GetProjectedTeamWidePassiveWeight(string skillName, PlayerPowerAnalyzerV2Request request)
        {
            var normalized = skillName.ToLowerInvariant();
            if (normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 0.45 : 3.15;
            if (normalized.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 3.15 : 0.45;
            if (normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)) return 2.55;
            if (normalized.Contains("ability (all allies)", StringComparison.OrdinalIgnoreCase) && request.EnemyWeakness != Element.None && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) return 3.05;
            if (normalized.Contains("boost pdef (all allies)", StringComparison.OrdinalIgnoreCase) || normalized.Contains("boost mdef (all allies)", StringComparison.OrdinalIgnoreCase)) return 2.0;
            if (normalized.Contains("all allies", StringComparison.OrdinalIgnoreCase)) return 2.4;
            return 0.55;
        }

        private static double ScorePassiveSkill(string skillName, int points, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            if (points <= 0)
            {
                return 0;
            }

            var scaledValue = TryGetPassiveBonusValue(skillName, points, out var bonusValue)
                ? bonusValue
                : points;

            return scaledValue * (IsTeamWidePassive(skillName)
                ? GetProjectedTeamWidePassiveWeight(skillName, request)
                : GetPassiveWeight(skillName, role, request));
        }

        private static bool TryGetPassiveBonusValue(string skillName, int points, out double bonusValue)
        {
            bonusValue = 0;
            if (points <= 0 || string.IsNullOrWhiteSpace(skillName))
            {
                return false;
            }

            var normalized = skillName.Trim();
            if (normalized.Contains("Boost PATK (All Allies)", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Boost MATK (All Allies)", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, StandardBreakpointPoints, BoostPatkAndMatkAllAlliesBonuses);
                return true;
            }

            if (normalized.Contains("Boost ATK (All Allies)", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, StandardBreakpointPoints, BoostAtkAllAlliesBonuses);
                return true;
            }

            if (normalized.Contains("Boost PDEF (All Allies)", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Boost MDEF (All Allies)", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, BoostPdefAndMdefAllAlliesBreakpointPoints, BoostPdefAndMdefAllAlliesBonuses);
                return true;
            }

            if (normalized.Contains("Ability (All Allies)", StringComparison.OrdinalIgnoreCase)
                && ContainsElementName(normalized))
            {
                bonusValue = ResolveBreakpointBonus(points, ElementAbilityAllAlliesBreakpointPoints, ElementAbilityAllAlliesBonuses);
                return true;
            }

            if (normalized.Contains("Boost Phys. Ability Pot", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Boost Mag. Ability Pot", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, StandardBreakpointPoints, BoostPhysAndMagAbilityPotBonuses);
                return true;
            }

            if (normalized.Contains("Boost Ability Pot", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, StandardBreakpointPoints, BoostAbilityPotBonuses);
                return true;
            }

            if (normalized.Contains("Boost ATK", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, StandardBreakpointPoints, BoostAtkBonuses);
                return true;
            }

            if (normalized.Contains("Boost PATK", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Boost MATK", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, StandardBreakpointPoints, BoostPatkAndMatkBonuses);
                return true;
            }

            if (normalized.Contains("Pot.", StringComparison.OrdinalIgnoreCase)
                && ContainsElementName(normalized))
            {
                bonusValue = ResolveBreakpointBonus(points, ElementPotBreakpointPoints, ElementPotBonuses);
                return true;
            }

            return false;
        }

        private static double ResolveBreakpointBonus(int points, IReadOnlyList<int> breakpoints, IReadOnlyList<double> bonuses)
        {
            var appliedBonus = 0d;
            for (var i = 0; i < breakpoints.Count && i < bonuses.Count; i++)
            {
                if (points < breakpoints[i])
                {
                    break;
                }

                appliedBonus = bonuses[i];
            }

            return appliedBonus;
        }

        private static bool ContainsElementName(string value)
        {
            return value.Contains("Fire", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Ice", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Lightning", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Water", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Wind", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Earth", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Holy", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Dark", StringComparison.OrdinalIgnoreCase);
        }

        private static double GetPassiveWeight(string skillName, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            var normalized = skillName.ToLowerInvariant();
            if (normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 1.6 : 3.0;
            if (normalized.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 3.0 : 1.6;
            if (normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)) return 2.5;
            if (normalized.Contains("ability (all allies)", StringComparison.OrdinalIgnoreCase) && request.EnemyWeakness != Element.None && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) return 3.0;
            if (normalized.Contains("boost pdef (all allies)", StringComparison.OrdinalIgnoreCase) || normalized.Contains("boost mdef (all allies)", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.DPS ? 1.2 : 2.2;
            if (normalized.Contains("all allies", StringComparison.OrdinalIgnoreCase)) return 2.9;
            if (role != CharacterRole.DPS && TryGetNonDpsSelfOnlyOffensivePassiveWeight(normalized, request, out var nonDpsOffensiveWeight)) return nonDpsOffensiveWeight;
            if (normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 0.8 : 2.0;
            if (normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 2.0 : 0.8;
            if (normalized.Contains("boost atk", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.Healer ? 0.9 : 1.35;
            if (normalized.Contains("phys. ability pot", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 0.8 : 2.3;
            if (normalized.Contains("mag. ability pot", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 2.3 : 0.8;
            if (normalized.Contains("ability pot", StringComparison.OrdinalIgnoreCase)) return 1.85;
            if (request.EnemyWeakness != Element.None && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) return 2.4;
            if (normalized.Contains("boost hp", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.Tank ? 1.6 : 1.0;
            if (normalized.Contains("heal", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.Healer ? 2.0 : 0.9;
            if (normalized.Contains("pdef", StringComparison.OrdinalIgnoreCase) || normalized.Contains("mdef", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.DPS ? 0.6 : 1.2;
            return 0.55;
        }

        private static bool TryGetNonDpsSelfOnlyOffensivePassiveWeight(string normalizedSkillName, PlayerPowerAnalyzerV2Request request, out double weight)
        {
            weight = 0;
            if (normalizedSkillName.Contains("all allies", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (normalizedSkillName.Contains("boost patk", StringComparison.OrdinalIgnoreCase))
            {
                weight = request.PreferredDamageType == DamageType.Magical ? 0.14 : 0.32;
                return true;
            }

            if (normalizedSkillName.Contains("boost matk", StringComparison.OrdinalIgnoreCase))
            {
                weight = request.PreferredDamageType == DamageType.Magical ? 0.32 : 0.14;
                return true;
            }

            if (normalizedSkillName.Contains("boost atk", StringComparison.OrdinalIgnoreCase))
            {
                weight = 0.28;
                return true;
            }

            if (normalizedSkillName.Contains("phys. ability pot", StringComparison.OrdinalIgnoreCase))
            {
                weight = request.PreferredDamageType == DamageType.Magical ? 0.1 : 0.24;
                return true;
            }

            if (normalizedSkillName.Contains("mag. ability pot", StringComparison.OrdinalIgnoreCase))
            {
                weight = request.PreferredDamageType == DamageType.Magical ? 0.24 : 0.1;
                return true;
            }

            if (normalizedSkillName.Contains("ability pot", StringComparison.OrdinalIgnoreCase))
            {
                weight = 0.22;
                return true;
            }

            if (normalizedSkillName.Contains("pot.", StringComparison.OrdinalIgnoreCase) && ContainsElementName(normalizedSkillName))
            {
                weight = request.EnemyWeakness != Element.None && normalizedSkillName.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
                    ? 0.28
                    : 0.12;
                return true;
            }

            return false;
        }

        private double ScoreDamage(OwnedWeaponCandidate weapon, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            var damageWeight = role == CharacterRole.DPS ? 0.34 : 0.15;
            var typeMultiplier = request.PreferredDamageType == DamageType.Any || weapon.Item.AbilityType.Equals(request.PreferredDamageType.ToString(), StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.65;
            var elementMultiplier = request.EnemyWeakness == Element.None || weapon.Item.Element.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase) || weapon.Item.Element.Equals("None", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.55;
            return weapon.Snapshot.DamagePercent * damageWeight * typeMultiplier * elementMultiplier;
        }

        private static double PreferredCoverageBonus(IEnumerable<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request)
        {
            var hard = providedEffectKeys.Count(k => request.HardRequiredEffectKeys.Contains(k, StringComparer.OrdinalIgnoreCase));
            var soft = providedEffectKeys.Count(k => request.SoftPreferredEffectKeys.Contains(k, StringComparer.OrdinalIgnoreCase));
            return (hard * 120) + (soft * 60);
        }

        private static double ScoreEffectKey(string key, CharacterRole role, PlayerPowerAnalyzerV2Request request, bool duplicate)
        {
            double baseScore = key switch
            {
                "elemental_resistance_down" => 220,
                "elemental_damage_received_up" => 180,
                "elemental_damage_up" => 165,
                "elemental_damage_bonus" => 150,
                "elemental_weapon_boost" => 150,
                "stat_debuff_tier_increase" => role == CharacterRole.DPS ? 120 : 170,
                "pdef_down" or "mdef_down" => 135,
                "patk_down" or "matk_down" => 110,
                "pdef_up" or "mdef_up" => role == CharacterRole.DPS ? 75 : 110,
                "patk_up" or "matk_up" => 115,
                "haste" => 115,
                "exploit_weakness" => 130,
                "enfeeble" => 115,
                "enliven" => 95,
                "torpor" => 120,
                "healing_support" => role == CharacterRole.Healer ? 140 : 85,
                _ => 80
            };
            if (request.HardRequiredEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) baseScore *= 1.9;
            if (request.SoftPreferredEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) baseScore *= 1.35;
            if (duplicate) baseScore *= 0.55;
            return baseScore;
        }

        private static double ScoreTeamEffects(HashSet<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request)
        {
            return providedEffectKeys.Sum(key => ScoreEffectKey(key, CharacterRole.Support, request, false));
        }

        private ReferenceTuningProfile BuildReferenceTuningProfile(PlayerPowerAnalyzerV2Request request)
        {
            var summaries = _maxDamageReferenceCatalog.GetArchetypeSummaries()
                .Where(summary => summary.EnemyWeakness == request.EnemyWeakness && summary.PreferredDamageType == request.PreferredDamageType)
                .ToList();
            if (summaries.Count == 0)
            {
                summaries = _maxDamageReferenceCatalog.GetArchetypeSummaries()
                    .Where(summary => summary.EnemyWeakness == request.EnemyWeakness || summary.PreferredDamageType == request.PreferredDamageType)
                    .ToList();
            }

            if (summaries.Count == 0)
            {
                return new ReferenceTuningProfile();
            }

            var totalTeams = summaries.Sum(summary => summary.TeamCount);
            if (totalTeams <= 0)
            {
                return new ReferenceTuningProfile();
            }

            return new ReferenceTuningProfile
            {
                SupportDebuffSetupRatio = summaries.Sum(summary => summary.TeamsWithSupportOrHealerDebuffSeedSetup) / (double)totalTeams,
                TripleElementDpsRatio = summaries.Sum(summary => summary.TeamsWithAnyTripleElementDpsLoadout) / (double)totalTeams,
                DebuffAmplifierRatio = summaries.Sum(summary => summary.TeamsWithStatDebuffTierIncreaseSource) / (double)totalTeams
            };
        }

        private double ScoreEffectKeyWithReferenceTuning(string key, CharacterRole role, PlayerPowerAnalyzerV2Request request, bool duplicate, ReferenceTuningProfile referenceTuningProfile)
        {
            var score = ScoreEffectKey(key, role, request, duplicate);
            if (referenceTuningProfile.SupportDebuffSetupRatio >= 0.35 && role is CharacterRole.Support or CharacterRole.Healer)
            {
                if (key is "elemental_resistance_down" or "pdef_down" or "mdef_down")
                {
                    score *= 1.0 + (0.18 * referenceTuningProfile.SupportDebuffSetupRatio);
                }
            }

            if (referenceTuningProfile.DebuffAmplifierRatio >= 0.35 && role is CharacterRole.Support or CharacterRole.Healer)
            {
                if (key == "stat_debuff_tier_increase")
                {
                    score *= 1.0 + (0.24 * referenceTuningProfile.DebuffAmplifierRatio);
                }
            }

            if (referenceTuningProfile.TripleElementDpsRatio >= 0.35 && role == CharacterRole.DPS)
            {
                if (key is "elemental_damage_up" or "elemental_damage_bonus" or "elemental_weapon_boost" or "elemental_damage_received_up")
                {
                    score *= 1.0 + (0.12 * referenceTuningProfile.TripleElementDpsRatio);
                }
            }

            return score;
        }

        private double ScoreDamageWithReferenceTuning(OwnedWeaponCandidate weapon, CharacterRole role, PlayerPowerAnalyzerV2Request request, ReferenceTuningProfile referenceTuningProfile)
        {
            var score = ScoreDamage(weapon, role, request);
            if (role == CharacterRole.DPS
                && referenceTuningProfile.TripleElementDpsRatio >= 0.35
                && request.EnemyWeakness != Element.None
                && weapon.Item.Element.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                score *= 1.0 + (0.14 * referenceTuningProfile.TripleElementDpsRatio);
            }

            return score;
        }

        private static double ScoreReferencePatternSynergyBonus(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            IReadOnlyCollection<string> providedEffectKeys,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile)
        {
            var bonus = 0d;
            if (referenceTuningProfile.SupportDebuffSetupRatio >= 0.35)
            {
                var supportHasDebuffSetup = baseVariants.Any(variant => variant.Role is CharacterRole.Support or CharacterRole.Healer
                    && variant.ProvidedEffectKeys.Overlaps(new[] { "elemental_resistance_down", "pdef_down", "mdef_down" }));
                if (supportHasDebuffSetup)
                {
                    bonus += 55 * referenceTuningProfile.SupportDebuffSetupRatio;
                }
                else if (CanAssumeStandardSynthDebuffSeedSetup(baseVariants))
                {
                    bonus += 24 * referenceTuningProfile.SupportDebuffSetupRatio;
                }
            }

            if (referenceTuningProfile.DebuffAmplifierRatio >= 0.35
                && providedEffectKeys.Contains("stat_debuff_tier_increase", StringComparer.OrdinalIgnoreCase)
                && providedEffectKeys.Any(key => key is "pdef_down" or "mdef_down" or "patk_down" or "matk_down" or "elemental_resistance_down"))
            {
                bonus += 80 * referenceTuningProfile.DebuffAmplifierRatio;
            }

            if (referenceTuningProfile.TripleElementDpsRatio >= 0.35 && request.EnemyWeakness != Element.None)
            {
                var dpsWeaknessMatches = baseVariants.Count(variant => variant.Role == CharacterRole.DPS
                    && (variant.MainWeapon.Element.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase)
                        || (variant.OffHandWeapon?.Element?.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase) ?? false)
                        || (variant.UltimateWeapon?.Element?.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase) ?? false)));
                if (dpsWeaknessMatches > 0)
                {
                    bonus += 28 * referenceTuningProfile.TripleElementDpsRatio * dpsWeaknessMatches;
                }
                else if (CanAssumeStandardSynthElementMateria(baseVariants, request))
                {
                    bonus += 12 * referenceTuningProfile.TripleElementDpsRatio;
                }
            }

            return bonus;
        }

        private static bool CanAssumeStandardSynthDebuffSeedSetup(IReadOnlyList<CharacterBuildCandidate> baseVariants)
        {
            return baseVariants.Any(variant => variant.Role is CharacterRole.Support or CharacterRole.Healer);
        }

        private static bool CanAssumeStandardSynthElementMateria(IReadOnlyList<CharacterBuildCandidate> baseVariants, PlayerPowerAnalyzerV2Request request)
        {
            return request.EnemyWeakness != Element.None
                && baseVariants.Any(variant => variant.Role == CharacterRole.DPS);
        }

        private SelectedCustomization SelectBestCustomization(OwnedWeaponCandidate weapon, CharacterRole role, PlayerPowerAnalyzerV2Request request, ReferenceTuningProfile referenceTuningProfile, double slotMultiplier, bool includeActiveEffects)
        {
            var best = new SelectedCustomization();
            foreach (var customization in weapon.Snapshot.Customizations)
            {
                var passiveScore = 0d;
                if (!string.IsNullOrWhiteSpace(customization.PassiveSkillName) && customization.PassiveSkillPoints > 0)
                {
                    var appliedPoints = Math.Max(0, (int)Math.Floor(customization.PassiveSkillPoints * slotMultiplier));
                    passiveScore += ScorePassiveSkill(customization.PassiveSkillName, appliedPoints, role, request);
                }

                var effectScore = 0d;
                if (includeActiveEffects && !string.IsNullOrWhiteSpace(customization.Description))
                {
                    foreach (var key in DetectEffectKeys(Array.Empty<string>(), customization.Description, request, request.BossImmunityKeys))
                    {
                        effectScore += ScoreEffectKeyWithReferenceTuning(key, role, request, false, referenceTuningProfile);
                    }
                }

                var score = passiveScore + effectScore;
                if (score <= best.Score)
                {
                    continue;
                }

                best = new SelectedCustomization
                {
                    Description = customization.Description,
                    PassiveSkillName = customization.PassiveSkillName,
                    PassiveSkillPoints = customization.PassiveSkillPoints,
                    EffectScore = effectScore,
                    Score = score
                };
            }

            return best;
        }

        private static IEnumerable<string> DetectEffectKeys(IEnumerable<string> effectTags, string abilityText, PlayerPowerAnalyzerV2Request request, IEnumerable<string> bossImmunityKeys)
        {
            var blob = string.Join(" | ", effectTags.Where(t => !string.IsNullOrWhiteSpace(t)).Append(abilityText ?? string.Empty));
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (request.EnemyWeakness != Element.None)
            {
                var element = request.EnemyWeakness.ToString();
                if (blob.Contains($"{element} Resistance Down", StringComparison.OrdinalIgnoreCase) || blob.Contains($"Status Ailment: {element} Weakness", StringComparison.OrdinalIgnoreCase)) keys.Add("elemental_resistance_down");
                if (blob.Contains($"{element} Damage Up", StringComparison.OrdinalIgnoreCase)) keys.Add("elemental_damage_up");
                if (blob.Contains($"{element} Damage Received Up", StringComparison.OrdinalIgnoreCase)) keys.Add("elemental_damage_received_up");
                if (blob.Contains($"{element} Damage Bonus", StringComparison.OrdinalIgnoreCase)) keys.Add("elemental_damage_bonus");
                if (blob.Contains($"{element} Weapon Boost", StringComparison.OrdinalIgnoreCase)) keys.Add("elemental_weapon_boost");
            }

            if (blob.Contains("PATK Up", StringComparison.OrdinalIgnoreCase)) keys.Add("patk_up");
            if (blob.Contains("MATK Up", StringComparison.OrdinalIgnoreCase)) keys.Add("matk_up");
            if (blob.Contains("PDEF Up", StringComparison.OrdinalIgnoreCase)) keys.Add("pdef_up");
            if (blob.Contains("MDEF Up", StringComparison.OrdinalIgnoreCase)) keys.Add("mdef_up");
            if (blob.Contains("PATK Down", StringComparison.OrdinalIgnoreCase)) keys.Add("patk_down");
            if (blob.Contains("MATK Down", StringComparison.OrdinalIgnoreCase)) keys.Add("matk_down");
            if (blob.Contains("PDEF Down", StringComparison.OrdinalIgnoreCase)) keys.Add("pdef_down");
            if (blob.Contains("MDEF Down", StringComparison.OrdinalIgnoreCase)) keys.Add("mdef_down");
            if (blob.Contains("Applied Stats Debuff Tier Increased", StringComparison.OrdinalIgnoreCase)
                || blob.Contains("Stats Debuff Tier Increased", StringComparison.OrdinalIgnoreCase)) keys.Add("stat_debuff_tier_increase");
            if (blob.Contains("Haste", StringComparison.OrdinalIgnoreCase)) keys.Add("haste");
            if (blob.Contains("Exploit Weakness", StringComparison.OrdinalIgnoreCase)) keys.Add("exploit_weakness");
            if (blob.Contains("Enfeeble", StringComparison.OrdinalIgnoreCase)) keys.Add("enfeeble");
            if (blob.Contains("Enliven", StringComparison.OrdinalIgnoreCase)) keys.Add("enliven");
            if (blob.Contains("Torpor", StringComparison.OrdinalIgnoreCase)) keys.Add("torpor");
            if (blob.Contains("Heal", StringComparison.OrdinalIgnoreCase) || blob.Contains("HP Recovery", StringComparison.OrdinalIgnoreCase)) keys.Add("healing_support");
            return keys.Where(key => !IsSuppressedByImmunity(key, bossImmunityKeys));
        }

        private static bool IsSuppressedByImmunity(string key, IEnumerable<string> bossImmunityKeys)
        {
            var immunitySet = new HashSet<string>(bossImmunityKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (immunitySet.Contains(key)) return true;
            if (key == "stat_debuff_tier_increase"
                && (immunitySet.Contains("stat_debuffs")
                    || (immunitySet.Contains("patk_down")
                        && immunitySet.Contains("matk_down")
                        && immunitySet.Contains("pdef_down")
                        && immunitySet.Contains("mdef_down")))) return true;
            if (immunitySet.Contains("stat_debuffs") && (key is "patk_down" or "matk_down" or "pdef_down" or "mdef_down")) return true;
            if (immunitySet.Contains("status_ailments") && (key is "exploit_weakness" or "enfeeble" or "enliven" or "torpor")) return true;
            if (immunitySet.Contains("elemental_setup") && (key is "elemental_resistance_down" or "elemental_damage_received_up")) return true;
            return false;
        }

        private static List<string> BuildSuppressedEffectNotes(IEnumerable<string> bossImmunityKeys)
        {
            var notes = new List<string>();
            foreach (var key in (bossImmunityKeys ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                notes.Add(key switch
                {
                    "patk_down" => "Boss immunities suppressed PATK Down effects.",
                    "matk_down" => "Boss immunities suppressed MATK Down effects.",
                    "pdef_down" => "Boss immunities suppressed PDEF Down effects.",
                    "mdef_down" => "Boss immunities suppressed MDEF Down effects.",
                    "stat_debuff_tier_increase" => "Boss immunities suppressed stat debuff tier increase effects.",
                    "elemental_resistance_down" => "Boss immunities suppressed elemental resistance down effects.",
                    "elemental_damage_received_up" => "Boss immunities suppressed elemental damage received up effects.",
                    "exploit_weakness" => "Boss immunities suppressed Exploit Weakness effects.",
                    "enfeeble" => "Boss immunities suppressed Enfeeble effects.",
                    "enliven" => "Boss immunities suppressed Enliven effects.",
                    "torpor" => "Boss immunities suppressed Torpor effects.",
                    "stat_debuffs" => "Boss immunities suppressed PATK/MATK/PDEF/MDEF down effects and stat debuff tier increase effects tied to them.",
                    "status_ailments" => "Boss immunities suppressed status-oriented effects such as Exploit Weakness, Enfeeble, Enliven, and Torpor.",
                    "elemental_setup" => "Boss immunities suppressed elemental setup effects such as resistance down and elemental damage received up.",
                    _ => $"Boss immunities suppressed {ToLabel(key)}."
                });
            }

            return notes;
        }

        private static string ToLabel(string key)
        {
            return AvailableEffectOptions.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Label
                ?? AvailableBossImmunityOptions.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Label
                ?? key;
        }

        private static int? ParseOwnedOverboost(string? ownership)
        {
            var normalized = (ownership ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "do-not-own") return null;
            if (normalized is "3-star" or "4-star" or "5-star") return 0;
            if (normalized.StartsWith("ob", StringComparison.OrdinalIgnoreCase) && int.TryParse(normalized[2..], out var overboost) && overboost >= 0 && overboost <= 10) return overboost;
            return null;
        }

        private int NormalizeOwnedLevel(int? level)
        {
            if (level.HasValue && level.Value > 0)
            {
                return level.Value;
            }

            return _weaponSearchDataService.MaxWeaponLevel;
        }

        private static bool HasTeamConflict(CharacterBuildCandidate first, CharacterBuildCandidate second)
        {
            return first.UsedItemNames.Overlaps(second.UsedItemNames);
        }

        private sealed class ReferenceTuningProfile
        {
            public double SupportDebuffSetupRatio { get; set; }
            public double TripleElementDpsRatio { get; set; }
            public double DebuffAmplifierRatio { get; set; }
        }

        private static string ResolveCharacterPortraitUrl(string character)
        {
            return CharacterPortraits.TryGetValue(character ?? string.Empty, out var portraitFileName)
                ? $"/images/characters/sm/{Uri.EscapeDataString(portraitFileName)}"
                : "/images/characters/sm/Cloud.jpg";
        }

        private sealed class LocalInventoryState
        {
            public Dictionary<string, LocalInventoryCostumeState> Costumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, LocalInventoryWeaponState> Weapons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class LocalInventoryCostumeState
        {
            public bool Owned { get; set; }
        }

        private sealed class LocalInventoryWeaponState
        {
            public string Ownership { get; set; } = string.Empty;
            public int? Level { get; set; }
        }

        private sealed class OwnedWeaponCandidate
        {
            public OwnedWeaponCandidate(WeaponSearchItem item, WeaponSnapshotResult snapshot, int overboostLevel, int level)
            {
                Item = item;
                Snapshot = snapshot;
                OverboostLevel = overboostLevel;
                Level = level;
            }

            public WeaponSearchItem Item { get; }
            public WeaponSnapshotResult Snapshot { get; }
            public int OverboostLevel { get; }
            public int Level { get; }
            public string Character => Item.Character;
            public bool IsUltimate => string.Equals(Item.EquipmentType, "Ultimate", StringComparison.OrdinalIgnoreCase) || Snapshot.EquipmentType.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class OwnedCostumeCandidate
        {
            public OwnedCostumeCandidate(WeaponSearchItem item)
            {
                Item = item;
            }

            public WeaponSearchItem Item { get; }
        }

        private sealed class SelectedCustomization
        {
            public string Description { get; set; } = string.Empty;
            public string? PassiveSkillName { get; set; }
            public int PassiveSkillPoints { get; set; }
            public double EffectScore { get; set; }
            public double Score { get; set; }
        }

        private sealed class SlotEvaluation
        {
            public string Name { get; set; } = string.Empty;
            public PlayerPowerAnalyzerV2ItemSlot Slot { get; set; } = new();
            public Dictionary<string, int> PassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> ProvidedEffectKeys { get; set; } = new();
            public double NonPassiveScore { get; set; }
            public double Score { get; set; }
        }

        private sealed class CharacterBuildCandidate
        {
            public string CharacterName { get; set; } = string.Empty;
            public CharacterRole Role { get; set; }
            public double BaseScore { get; set; }
            public double NonPassiveScore { get; set; }
            public PlayerPowerAnalyzerV2ItemSlot MainWeapon { get; set; } = new();
            public Dictionary<string, int> MainPassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public PlayerPowerAnalyzerV2ItemSlot? OffHandWeapon { get; set; }
            public Dictionary<string, int> OffPassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public PlayerPowerAnalyzerV2ItemSlot? UltimateWeapon { get; set; }
            public Dictionary<string, int> UltimatePassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public PlayerPowerAnalyzerV2ItemSlot? MainOutfit { get; set; }
            public Dictionary<string, int> MainOutfitPassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<PlayerPowerAnalyzerV2ItemSlot> SubOutfits { get; set; } = new();
            public Dictionary<string, int> SubOutfitPassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ProvidedEffectKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> UsedItemNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> PassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            public PlayerPowerAnalyzerV2CharacterBuild ToOutput()
            {
                var patk = MainWeapon.Patk + (OffHandWeapon?.Patk ?? 0) + (UltimateWeapon?.Patk ?? 0) + SubOutfits.Sum(x => x.Patk);
                var matk = MainWeapon.Matk + (OffHandWeapon?.Matk ?? 0) + (UltimateWeapon?.Matk ?? 0) + SubOutfits.Sum(x => x.Matk);
                var heal = MainWeapon.Heal + (OffHandWeapon?.Heal ?? 0) + (UltimateWeapon?.Heal ?? 0) + SubOutfits.Sum(x => x.Heal);
                return new PlayerPowerAnalyzerV2CharacterBuild
                {
                    CharacterName = CharacterName,
                    Role = Role,
                    CharacterPortraitUrl = ResolveCharacterPortraitUrl(CharacterName),
                    Score = Math.Round(BaseScore, 2),
                    TotalPatk = patk,
                    TotalMatk = matk,
                    TotalHeal = heal,
                    MainWeapon = MainWeapon,
                    OffHandWeapon = OffHandWeapon,
                    UltimateWeapon = UltimateWeapon,
                    MainOutfit = MainOutfit,
                    SubOutfits = SubOutfits.ToList(),
                    ProvidedEffectLabels = ProvidedEffectKeys.Select(ToLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                };
            }
        }

        private sealed class SubWeaponAssignment
        {
            public string CharacterName { get; set; } = string.Empty;
            public SlotEvaluation Evaluation { get; set; } = new();
        }

        private sealed class TeamCandidate
        {
            public List<PlayerPowerAnalyzerV2CharacterBuild> Characters { get; set; } = new();
            public string? TemplateName { get; set; }
            public double Score { get; set; }
            public List<string> MatchedRequiredEffects { get; set; } = new();
            public List<string> MissingRequiredEffects { get; set; } = new();
            public List<string> MatchedPreferredEffects { get; set; } = new();
            public List<string> MissingPreferredEffects { get; set; } = new();
            public List<string> ProvidedEffectKeys { get; set; } = new();
            public List<string> SuppressedEffectNotes { get; set; } = new();
            public List<string> DebugNotes { get; set; } = new();
            public string TeamKey { get; set; } = string.Empty;
        }
    }
}
