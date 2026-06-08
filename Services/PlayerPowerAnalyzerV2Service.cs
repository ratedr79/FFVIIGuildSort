using System.Collections.Concurrent;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed partial class PlayerPowerAnalyzerV2Service
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

        // An All-Allies offensive passive feeds the whole offensive core, so per-target % is multiplied by
        // a proxy for how many offensive allies benefit (refined by real team composition at team scoring).
        private const double AllAlliesOffensiveBeneficiaryProxy = 2.0;

        private static readonly int[] StandardBreakpointPoints = [1, 5, 15, 25, 35, 45, 55];
        private static readonly double[] BoostPatkAndMatkBonuses = [5, 10, 15, 20, 30, 40, 50];
        private static readonly double[] BoostAbilityPotBonuses = [3, 8, 15, 22, 30, 35, 40];
        private static readonly double[] ElementPotBonuses = [6, 15, 25, 40, 55, 70, 85, 100, 110, 120];
        private static readonly int[] ElementPotBreakpointPoints = [1, 5, 15, 25, 35, 45, 55, 65, 80, 100];
        private static readonly double[] BoostPhysAndMagAbilityPotBonuses = [5, 15, 30, 45, 60, 70, 80];
        private static readonly double[] ElementAbilityAllAlliesBonuses = [5, 10, 15];
        private static readonly int[] ElementAbilityAllAlliesBreakpointPoints = [1, 5, 15];
        private static readonly double[] ElementPotArcanumAllAlliesBonuses = [5, 10, 15, 30];
        private static readonly int[] ElementPotArcanumAllAlliesBreakpointPoints = [1, 5, 15, 45];
        private static readonly double[] BoostPatkAndMatkAllAlliesBonuses = [5, 10, 14, 18, 22, 25, 28];
        private static readonly double[] BoostAtkAllAlliesBonuses = [3, 5, 7, 9, 11, 13, 14];
        private static readonly double[] BoostAtkBonuses = [3, 5, 7, 10, 15, 20, 25];
        private static readonly double[] BoostPdefAndMdefAllAlliesBonuses = [5, 10, 20, 30, 40];
        private static readonly int[] BoostPdefAndMdefAllAlliesBreakpointPoints = [1, 5, 15, 25, 35];
        private static readonly double[] BuffDebuffExtensionBonuses = [1, 2, 3, 4, 5, 6, 7];
        private static readonly double[] BoostHealBonuses = [5, 15, 30, 45, 60, 70, 80, 90, 95, 100];
        private static readonly int[] BoostHealBreakpointPoints = [1, 5, 15, 25, 35, 45, 55, 65, 80, 100];
        private static readonly Regex PotencyMarkerRegex = new(@"\[Pot:\s*(?<value>-?\d+(?:\.\d+)?)%?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Ability amplification uses a distinct marker shape, e.g. "[Damage +40.0% up to 1 time(s)]".
        private static readonly Regex AbilityAmplificationMarkerRegex = new(@"\[Damage\s*\+(?<value>-?\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AbilityAmplificationCountRegex = new(@"up to\s*(?<value>\d+)\s*time", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DurationMarkerRegex = new(@"\[Dur:\s*(?<value>-?\d+(?:\.\d+)?)s?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ExtensionMarkerRegex = new(@"\[Ext:\s*(?<value>-?\d+(?:\.\d+)?)s?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RangeMarkerRegex = new(@"\[(?:Rng\.?|Range):\s*(?<value>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> StageOneOffensiveFamilies = new(StringComparer.OrdinalIgnoreCase)
        {
            "attack_buff",
            "defense_debuff",
            "elemental_resistance_debuff",
            "damage_bonus",
            "weapon_boost",
            "damage_up",
            "damage_received_up",
            "exploit_weakness",
            "ability_amplification",
            "torpor"
        };
        private static readonly HashSet<string> MateriaAssumableFamilies = new(StringComparer.OrdinalIgnoreCase)
        {
            "attack_buff",
            "defense_debuff",
            "elemental_resistance_debuff"
        };
        private const int AdaptiveSearchMinimumStandardWeaponsPerCharacter = 6;
        private const int AdaptiveSearchRetainedLowPriorityWeaponsPerCharacter = 1;
        private const int DefaultMainWeaponOptionsPerCharacter = 4;
        private const int DefaultOffHandWeaponOptionsPerCharacter = 3;
        private const int DefaultUltimateOptionsPerCharacter = 2;
        private const int DefaultMainOutfitOptionsPerCharacter = 3;
        private const int DefaultSubOutfitOptionsPerCharacter = 2;
        private const int DefaultRetainedVariantsPerCharacter = 6;
        private const int DefaultSkeletonExpansionLimit = 24;
        private const int ExhaustiveMainWeaponOptionsPerCharacter = 6;
        private const int ExhaustiveOffHandWeaponOptionsPerCharacter = 4;
        private const int ExhaustiveUltimateOptionsPerCharacter = 3;
        private const int ExhaustiveMainOutfitOptionsPerCharacter = 4;
        private const int ExhaustiveSubOutfitOptionsPerCharacter = 2;
        private const int ExhaustiveRetainedVariantsPerCharacter = 8;
        private const int ExhaustiveSkeletonExpansionLimit = 40;
        private const int AdaptiveWideRosterThreshold = 9;
        private const int AdaptiveVeryWideRosterThreshold = 12;
        private const int AdaptiveWideRosterCharacterShortlistLimit = 10;
        private const int AdaptiveVeryWideRosterCharacterShortlistLimit = 8;
        private const int AdaptiveWideRosterSkeletonExpansionLimit = 18;
        private const int AdaptiveVeryWideRosterSkeletonExpansionLimit = 12;
        private const int AdaptiveShortlistMinimumDpsCount = 2;
        private const int AdaptiveShortlistMinimumHealerCount = 1;
        private const int AdaptiveShortlistMinimumSupportCount = 1;
        private const int AdaptiveShortlistMinimumTankCount = 1;

        private enum OffensiveRoleProfile
        {
            Carry,
            OffensiveEnabler,
            SustainSupport,
            DefensiveSupport
        }

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
            new() { Key = "phys_damage_bonus", Label = "Phys. Damage Bonus", Group = "Damage Support" },
            new() { Key = "mag_damage_bonus", Label = "Mag. Damage Bonus", Group = "Damage Support" },
            new() { Key = "phys_weapon_boost", Label = "Phys. Weapon Boost", Group = "Damage Support" },
            new() { Key = "mag_weapon_boost", Label = "Mag. Weapon Boost", Group = "Damage Support" },
            new() { Key = "phys_damage_received_up", Label = "Phys. Damage Received Up", Group = "Damage Support" },
            new() { Key = "mag_damage_received_up", Label = "Mag. Damage Received Up", Group = "Damage Support" },
            new() { Key = "gear_c_ability_uses", Label = "Gear C. Ability Uses", Group = "Status / Utility" },
            new() { Key = "patk_up", Label = "PATK Up", Group = "Buffs / Debuffs" },
            new() { Key = "matk_up", Label = "MATK Up", Group = "Buffs / Debuffs" },
            new() { Key = "pdef_up", Label = "PDEF Up", Group = "Buffs / Debuffs" },
            new() { Key = "mdef_up", Label = "MDEF Up", Group = "Buffs / Debuffs" },
            new() { Key = "patk_down", Label = "PATK Down", Group = "Buffs / Debuffs" },
            new() { Key = "matk_down", Label = "MATK Down", Group = "Buffs / Debuffs" },
            new() { Key = "pdef_down", Label = "PDEF Down", Group = "Buffs / Debuffs" },
            new() { Key = "mdef_down", Label = "MDEF Down", Group = "Buffs / Debuffs" },
            new() { Key = "stat_debuff_tier_increase", Label = "Applied Stats Debuff Tier Increased", Group = "Buffs / Debuffs" },
            new() { Key = "stat_buff_tier_increase", Label = "Applied Stats Buff Tier Increased", Group = "Buffs / Debuffs" },
            new() { Key = "haste", Label = "Haste", Group = "Status / Utility" },
            new() { Key = "atb_conservation", Label = "ATB Conservation", Group = "Status / Utility" },
            new() { Key = "atb_gain", Label = "ATB Gain", Group = "Status / Utility" },
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
            var weaponSlotEvaluationCache = new ConcurrentDictionary<string, SlotEvaluation>(StringComparer.OrdinalIgnoreCase);
            var costumeSlotEvaluationCache = new ConcurrentDictionary<string, SlotEvaluation>(StringComparer.OrdinalIgnoreCase);

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

            var searchModeFilter = ApplySearchModeWeaponFiltering(ownedWeapons, request);
            ownedWeapons = searchModeFilter.Weapons;
            if (searchModeFilter.TrimmedWeaponCount > 0)
            {
                result.Warnings.Add($"Adaptive search trimmed {searchModeFilter.TrimmedWeaponCount} Event/Grindable weapon{(searchModeFilter.TrimmedWeaponCount == 1 ? string.Empty : "s")} across {searchModeFilter.AffectedCharacterCount} character{(searchModeFilter.AffectedCharacterCount == 1 ? string.Empty : "s")}. Switch Search Mode to Exhaustive to include every owned weapon.");
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
            var charactersWithMainWeapon = ownedWeaponsByCharacter.Keys
                .OrderBy(character => character, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (charactersWithMainWeapon.Count < 3)
            {
                result.IsPlaceholder = true;
                result.FailureReason = $"At least 3 characters with owned non-ultimate weapons are needed for V2 analysis. Current local inventory coverage: {charactersWithMainWeapon.Count}.";
                return result;
            }

            var adaptiveSearchProfile = BuildAdaptiveSearchProfile(charactersWithMainWeapon.Count, request);
            if (adaptiveSearchProfile.IsVariantBreadthReduced)
            {
                result.Warnings.Add($"Adaptive search narrowed wide-roster variant generation to reduce combinations (main/off-hand/ultimate/outfit/sub-outfit = {adaptiveSearchProfile.MainWeaponOptionsPerCharacter}/{adaptiveSearchProfile.OffHandWeaponOptionsPerCharacter}/{adaptiveSearchProfile.UltimateOptionsPerCharacter}/{adaptiveSearchProfile.MainOutfitOptionsPerCharacter}/{adaptiveSearchProfile.SubOutfitOptionsPerCharacter}; retained variants per character = {adaptiveSearchProfile.RetainedVariantsPerCharacter}). Switch Search Mode to Exhaustive to restore full breadth.");
            }

            var seedEntries = new KeyValuePair<string, List<CharacterBuildCandidate>>[charactersWithMainWeapon.Count];
            Parallel.For(0, charactersWithMainWeapon.Count, CreateCpuBoundParallelOptions(), index =>
            {
                var character = charactersWithMainWeapon[index];
                var seedVariants = BuildCharacterSeedVariants(character, ownedWeaponsByCharacter, ultimateWeaponsByCharacter, ownedCostumesByCharacter, request, referenceTuningProfile, adaptiveSearchProfile, weaponSlotEvaluationCache, costumeSlotEvaluationCache);
                seedEntries[index] = new KeyValuePair<string, List<CharacterBuildCandidate>>(character, seedVariants);
            });
            var seedVariantsByCharacter = seedEntries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in seedVariantsByCharacter.Where(e => e.Value.Count == 0))
            {
                result.DebugNotes.Add($"{entry.Key}: no valid main-weapon seed variants were generated.");
            }

            var characterShortlistResult = ApplyAdaptiveCharacterShortlist(seedVariantsByCharacter, request, adaptiveSearchProfile);
            seedVariantsByCharacter = characterShortlistResult.VariantsByCharacter;
            if (characterShortlistResult.WasShortlisted)
            {
                result.Warnings.Add($"Adaptive search shortlisted {characterShortlistResult.RetainedCharacterCount} of {characterShortlistResult.OriginalCharacterCount} characters for skeleton generation on this wide roster while preserving role and requested-effect anchors. Switch Search Mode to Exhaustive to consider every owned character.");
            }

            var teamCandidates = BuildTeamCandidatesFromSkeletons(
                seedVariantsByCharacter,
                ownedWeaponsByCharacter,
                ultimateWeaponsByCharacter,
                ownedCostumesByCharacter,
                ownedWeapons,
                request,
                referenceTuningProfile,
                adaptiveSearchProfile,
                normalizedEnabledTemplateNames,
                mutuallyExclusiveCharacterGroups,
                weaponSlotEvaluationCache,
                costumeSlotEvaluationCache);
            if (teamCandidates.Count == 0)
            {
                result.FailureReason = request.HardRequiredEffectKeys.Count > 0
                    ? "No valid V2 team matched the selected hard-required effects with the current local inventory."
                    : "No valid V2 team could be assembled from the current local inventory.";
                return result;
            }

            var orderedTeamCandidates = OrderTeamCandidatesForSelection(teamCandidates).ToList();
            var best = orderedTeamCandidates[0];
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
            result.OffensiveAbilitySummary = best.OffensiveAbilitySummary;
            result.SuppressedEffectNotes = best.SuppressedEffectNotes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.DebugNotes = best.DebugNotes.ToList();
            result.ScoreBreakdown = CloneScoreBreakdown(best.ScoreBreakdown);
            var bestEffectKeys = new HashSet<string>(best.ProvidedEffectKeys, StringComparer.OrdinalIgnoreCase);
            result.AlternateTeams = OrderTeamCandidatesForSelection(orderedTeamCandidates
                .Where(t => !string.Equals(t.TeamKey, best.TeamKey, StringComparison.OrdinalIgnoreCase))
                .GroupBy(t => t.TeamKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => OrderTeamCandidatesForSelection(g).First()))
                .Take(5)
                .Select(t =>
                {
                    var altEffectKeys = new HashSet<string>(t.ProvidedEffectKeys, StringComparer.OrdinalIgnoreCase);
                    return new PlayerPowerAnalyzerV2AlternateTeam
                    {
                        Characters = t.Characters.Select(c => c.CharacterName).ToList(),
                        Score = Math.Round(t.Score, 2),
                        CharacterBuilds = t.Characters,
                        ProvidedEffectLabels = altEffectKeys.Select(ToLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                        AddsVsBest = altEffectKeys.Except(bestEffectKeys).Where(key => IsDamageRelevantEffectKey(key, request.PreferredDamageType)).Select(ToLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                        DropsVsBest = bestEffectKeys.Except(altEffectKeys).Where(key => IsDamageRelevantEffectKey(key, request.PreferredDamageType)).Select(ToLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                    };
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

        private static ParallelOptions CreateCpuBoundParallelOptions()
        {
            var maxDegreeOfParallelism = Environment.ProcessorCount > 1 ? Environment.ProcessorCount - 1 : 1;
            return new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            };
        }

        private SearchModeFilterResult ApplySearchModeWeaponFiltering(List<OwnedWeaponCandidate> ownedWeapons, PlayerPowerAnalyzerV2Request request)
        {
            if (request.SearchMode == PlayerPowerAnalyzerV2SearchMode.Exhaustive || ownedWeapons.Count == 0)
            {
                return new SearchModeFilterResult
                {
                    Weapons = ownedWeapons
                };
            }

            var filtered = new List<OwnedWeaponCandidate>();
            var trimmedWeaponCount = 0;
            var affectedCharacterCount = 0;
            foreach (var group in ownedWeapons.GroupBy(weapon => weapon.Character, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var weaponsForCharacter = group.ToList();
                var ultimateWeapons = weaponsForCharacter.Where(weapon => weapon.IsUltimate).ToList();
                var regularWeapons = weaponsForCharacter.Where(weapon => !weapon.IsUltimate).ToList();
                var filteredRegularWeapons = ApplyAdaptiveCharacterWeaponPruning(regularWeapons, request);
                var trimmedForCharacter = regularWeapons.Count - filteredRegularWeapons.Count;
                if (trimmedForCharacter > 0)
                {
                    trimmedWeaponCount += trimmedForCharacter;
                    affectedCharacterCount++;
                }

                filtered.AddRange(filteredRegularWeapons);
                filtered.AddRange(ultimateWeapons);
            }

            return new SearchModeFilterResult
            {
                Weapons = filtered,
                TrimmedWeaponCount = trimmedWeaponCount,
                AffectedCharacterCount = affectedCharacterCount
            };
        }

        private List<OwnedWeaponCandidate> ApplyAdaptiveCharacterWeaponPruning(List<OwnedWeaponCandidate> weapons, PlayerPowerAnalyzerV2Request request)
        {
            if (weapons.Count == 0)
            {
                return weapons;
            }

            var standardWeapons = weapons
                .Where(weapon => !IsAdaptiveLowPriorityWeaponType(weapon.Item.EquipmentType))
                .ToList();
            var lowPriorityWeapons = weapons
                .Where(weapon => IsAdaptiveLowPriorityWeaponType(weapon.Item.EquipmentType))
                .ToList();
            if (lowPriorityWeapons.Count == 0 || standardWeapons.Count < AdaptiveSearchMinimumStandardWeaponsPerCharacter)
            {
                return weapons;
            }

            var importantKeys = request.HardRequiredEffectKeys
                .Concat(request.SoftPreferredEffectKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keepIds = new HashSet<string>(standardWeapons.Select(weapon => weapon.Item.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var weapon in lowPriorityWeapons.Where(weapon => LowPriorityWeaponMatchesRequestedEffect(weapon, importantKeys, request)))
            {
                keepIds.Add(weapon.Item.Id);
            }

            var keptLowPriorityCount = lowPriorityWeapons.Count(weapon => keepIds.Contains(weapon.Item.Id));
            foreach (var weapon in OrderAdaptiveLowPriorityWeapons(lowPriorityWeapons))
            {
                if (keptLowPriorityCount >= AdaptiveSearchRetainedLowPriorityWeaponsPerCharacter)
                {
                    break;
                }

                if (keepIds.Add(weapon.Item.Id))
                {
                    keptLowPriorityCount++;
                }
            }

            return weapons
                .Where(weapon => keepIds.Contains(weapon.Item.Id))
                .ToList();
        }

        private bool LowPriorityWeaponMatchesRequestedEffect(OwnedWeaponCandidate weapon, IReadOnlySet<string> importantKeys, PlayerPowerAnalyzerV2Request request)
        {
            if (importantKeys.Count == 0)
            {
                return false;
            }

            var effects = DetectActiveEffects(
                weapon.Item.EffectTags,
                weapon.Snapshot.AbilityText,
                request,
                request.BossImmunityKeys,
                "Main Weapon",
                weapon.Item.Name,
                weapon.Item.AbilityType,
                weapon.Item.Element);
            return effects.Any(effect => importantKeys.Contains(effect.Key));
        }

        private static bool IsAdaptiveLowPriorityWeaponType(string equipmentType)
        {
            return string.Equals(equipmentType, "Event", StringComparison.OrdinalIgnoreCase)
                || string.Equals(equipmentType, "Grindable", StringComparison.OrdinalIgnoreCase);
        }

        private static IOrderedEnumerable<OwnedWeaponCandidate> OrderAdaptiveLowPriorityWeapons(IEnumerable<OwnedWeaponCandidate> weapons)
        {
            return weapons
                .OrderByDescending(weapon => weapon.OverboostLevel)
                .ThenByDescending(weapon => weapon.Level)
                .ThenByDescending(weapon => weapon.Snapshot.Patk + weapon.Snapshot.Matk + weapon.Snapshot.Heal)
                .ThenByDescending(weapon => weapon.Snapshot.DamagePercent)
                .ThenBy(weapon => weapon.Item.Name, StringComparer.OrdinalIgnoreCase);
        }

        private List<CharacterBuildCandidate> BuildCharacterVariants(
            string character,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ownedWeaponsByCharacter,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ultimateWeaponsByCharacter,
            IReadOnlyDictionary<string, List<OwnedCostumeCandidate>> ownedCostumesByCharacter,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            AdaptiveSearchProfile adaptiveSearchProfile,
            ConcurrentDictionary<string, SlotEvaluation> weaponSlotEvaluationCache,
            ConcurrentDictionary<string, SlotEvaluation> costumeSlotEvaluationCache,
            string? requiredMainWeaponName = null,
            bool allowMainWeaponSwap = true)
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
            var eligibleMainWeapons = string.IsNullOrWhiteSpace(requiredMainWeaponName)
                ? mainWeapons
                : mainWeapons
                    .Where(w => w.Item.Name.Equals(requiredMainWeaponName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (eligibleMainWeapons.Count == 0)
            {
                return new List<CharacterBuildCandidate>();
            }

            var mainOptions = eligibleMainWeapons
                .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Main Weapon", 1.0, true, true, weaponSlotEvaluationCache))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(string.IsNullOrWhiteSpace(requiredMainWeaponName) ? adaptiveSearchProfile.MainWeaponOptionsPerCharacter : eligibleMainWeapons.Count)
                .ToList();
            var ultimateOptions = ultimates
                .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Ultimate", 1.0, true, true, weaponSlotEvaluationCache))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(adaptiveSearchProfile.UltimateOptionsPerCharacter)
                .ToList();
            var costumeOptions = (costumes ?? new List<OwnedCostumeCandidate>())
                .Select(c => GetOrCreateCostumeSlot(c, role, request, referenceTuningProfile, "Main Outfit", 1.0, true, costumeSlotEvaluationCache))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(adaptiveSearchProfile.MainOutfitOptionsPerCharacter)
                .ToList();

            var variants = new List<CharacterBuildCandidate>();
            foreach (var main in mainOptions)
            {
                var offOptions = mainWeapons
                    .Where(w => !w.Item.Name.Equals(main.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Off-hand", 0.5, true, true, weaponSlotEvaluationCache))
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(adaptiveSearchProfile.OffHandWeaponOptionsPerCharacter)
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
                                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                                    .Take(adaptiveSearchProfile.SubOutfitOptionsPerCharacter)
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

                            var variant = ComposeCharacterVariantCandidate(
                                character,
                                role,
                                main,
                                off,
                                ultimate,
                                mainCostume,
                                subOutfits,
                                subOutfitPassivePoints,
                                subOutfitScore,
                                usedNames,
                                request);

                            if (allowMainWeaponSwap && off != null)
                            {
                                var currentMainWeapon = mainWeapons.FirstOrDefault(w => w.Item.Name.Equals(main.Name, StringComparison.OrdinalIgnoreCase));
                                var currentOffWeapon = mainWeapons.FirstOrDefault(w => w.Item.Name.Equals(off.Name, StringComparison.OrdinalIgnoreCase));
                                if (currentMainWeapon != null && currentOffWeapon != null)
                                {
                                    var swappedMain = GetOrCreateWeaponSlot(currentOffWeapon, role, request, referenceTuningProfile, "Main Weapon", 1.0, true, true, weaponSlotEvaluationCache);
                                    var swappedOff = GetOrCreateWeaponSlot(currentMainWeapon, role, request, referenceTuningProfile, "Off-hand", 0.5, true, true, weaponSlotEvaluationCache);
                                    var swappedVariant = ComposeCharacterVariantCandidate(
                                        character,
                                        role,
                                        swappedMain,
                                        swappedOff,
                                        ultimate,
                                        mainCostume,
                                        subOutfits,
                                        subOutfitPassivePoints,
                                        subOutfitScore,
                                        usedNames,
                                        request);

                                    // Item 3 Phase 2: when a character holds a substantial attacking weapon
                                    // (element/axis-gated), the main slot must hold the highest EFFECTIVE-damage
                                    // weapon — orient PURELY by damage and never let R-ability orientation pull a
                                    // real attacker out of main. (The loop iterates both orderings of a pair, so
                                    // without this block the R-ability comparison would flip Winged Chakram 1340%
                                    // Lightning back out for Bird of Prey 940% Water.) Pure support weapons (both
                                    // below the threshold) keep the R-ability orientation.
                                    var swappedMainEffectiveDamage = GetWeaponEffectiveDamagePercent(swappedMain.Slot, request);
                                    var currentMainEffectiveDamage = GetWeaponEffectiveDamagePercent(main.Slot, request);
                                    bool shouldSwap;
                                    if (request.PreferredDamageType != DamageType.Any
                                        && (swappedMainEffectiveDamage > AttackingWeaponDamageThreshold
                                            || currentMainEffectiveDamage > AttackingWeaponDamageThreshold))
                                    {
                                        shouldSwap = swappedMainEffectiveDamage > currentMainEffectiveDamage + 0.001;
                                    }
                                    else
                                    {
                                        shouldSwap = GetVariantSelectionScore(swappedVariant, request) > GetVariantSelectionScore(variant, request);
                                    }

                                    if (shouldSwap)
                                    {
                                        variant = swappedVariant;
                                    }
                                }
                            }

                            variants.Add(variant);
                        }
                    }
                }
            }

            return OrderCharacterVariantsForSelection(variants, request)
                .Take(adaptiveSearchProfile.RetainedVariantsPerCharacter)
                .ToList();
        }

        private List<CharacterBuildCandidate> BuildCharacterSeedVariants(
            string character,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ownedWeaponsByCharacter,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ultimateWeaponsByCharacter,
            IReadOnlyDictionary<string, List<OwnedCostumeCandidate>> ownedCostumesByCharacter,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            AdaptiveSearchProfile adaptiveSearchProfile,
            ConcurrentDictionary<string, SlotEvaluation> weaponSlotEvaluationCache,
            ConcurrentDictionary<string, SlotEvaluation> costumeSlotEvaluationCache)
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

            var subOutfits = new List<PlayerPowerAnalyzerV2ItemSlot>();
            var subOutfitPassivePoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rankedMainWeapons = mainWeapons
                .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Main Weapon", 1.0, true, true, weaponSlotEvaluationCache))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var retainedMainWeapons = rankedMainWeapons
                .Take(adaptiveSearchProfile.MainWeaponOptionsPerCharacter)
                .ToList();
            retainedMainWeapons.AddRange(rankedMainWeapons
                .Where(main => !retainedMainWeapons.Any(existing => existing.Name.Equals(main.Name, StringComparison.OrdinalIgnoreCase))
                    && ShouldPreserveContextualSupportSeedMain(main, role, request))
                .Take(2));

            // Candidate-gen rework target #1 — ULTIMATE-AWARE SEEDS. Previously seeds were main-weapon-only
            // (null off/ultimate/outfit), so the gating stage (shortlist/anchor/skeleton) judged a character on
            // a fraction of their kit and was BLIND to the ultimate — whose base stats + R-ability passives
            // ALWAYS apply, and whose presence is what lets the team scorer see amplifiers (Abraxas). Compose
            // each main-weapon seed with the character's BEST off-hand + ultimate + outfit so the seed reflects
            // real potential. Only the MAIN is carried into the downstream expansion (which re-explores all
            // off/ult/outfit combos on surviving seeds), so this sharpens GATING accuracy without locking builds.
            SlotEvaluation? BestUltimateSlot()
                => ultimates
                    .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Ultimate", 1.0, true, true, weaponSlotEvaluationCache))
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            SlotEvaluation? BestOutfitSlot()
                => (costumes ?? new List<OwnedCostumeCandidate>())
                    .Select(c => GetOrCreateCostumeSlot(c, role, request, referenceTuningProfile, "Main Outfit", 1.0, true, costumeSlotEvaluationCache))
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

            var bestUltimate = BestUltimateSlot();
            var bestOutfit = BestOutfitSlot();

            var seedVariants = retainedMainWeapons
                .Select(main =>
                {
                    var bestOff = mainWeapons
                        .Where(w => !w.Item.Name.Equals(main.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(w => GetOrCreateWeaponSlot(w, role, request, referenceTuningProfile, "Off-hand", 0.5, true, true, weaponSlotEvaluationCache))
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { main.Name };
                    if (bestOff != null) usedNames.Add(bestOff.Name);
                    if (bestUltimate != null) usedNames.Add(bestUltimate.Name);
                    if (bestOutfit != null) usedNames.Add(bestOutfit.Name);

                    return ComposeCharacterVariantCandidate(
                        character,
                        role,
                        main,
                        bestOff,
                        bestUltimate,
                        bestOutfit,
                        subOutfits,
                        subOutfitPassivePoints,
                        0d,
                        usedNames,
                        request);
                })
                .ToList();

            return OrderCharacterVariantsForSelection(seedVariants, request).ToList();
        }

        private static AdaptiveSearchProfile BuildAdaptiveSearchProfile(int rosterSize, PlayerPowerAnalyzerV2Request request)
        {
            var hasExplicitCoverageRequests = request.HardRequiredEffectKeys.Count > 0 || request.SoftPreferredEffectKeys.Count > 0;
            var profile = new AdaptiveSearchProfile
            {
                MainWeaponOptionsPerCharacter = DefaultMainWeaponOptionsPerCharacter,
                OffHandWeaponOptionsPerCharacter = DefaultOffHandWeaponOptionsPerCharacter,
                UltimateOptionsPerCharacter = DefaultUltimateOptionsPerCharacter,
                MainOutfitOptionsPerCharacter = DefaultMainOutfitOptionsPerCharacter,
                SubOutfitOptionsPerCharacter = DefaultSubOutfitOptionsPerCharacter,
                RetainedVariantsPerCharacter = DefaultRetainedVariantsPerCharacter,
                SkeletonExpansionLimit = DefaultSkeletonExpansionLimit,
                CharacterShortlistLimit = int.MaxValue
            };

            if (request.SearchMode == PlayerPowerAnalyzerV2SearchMode.Exhaustive)
            {
                profile.MainWeaponOptionsPerCharacter = ExhaustiveMainWeaponOptionsPerCharacter;
                profile.OffHandWeaponOptionsPerCharacter = ExhaustiveOffHandWeaponOptionsPerCharacter;
                profile.UltimateOptionsPerCharacter = ExhaustiveUltimateOptionsPerCharacter;
                profile.MainOutfitOptionsPerCharacter = ExhaustiveMainOutfitOptionsPerCharacter;
                profile.SubOutfitOptionsPerCharacter = ExhaustiveSubOutfitOptionsPerCharacter;
                profile.RetainedVariantsPerCharacter = ExhaustiveRetainedVariantsPerCharacter;
                profile.SkeletonExpansionLimit = ExhaustiveSkeletonExpansionLimit;
                return profile;
            }

            if (rosterSize >= AdaptiveVeryWideRosterThreshold)
            {
                profile.MainWeaponOptionsPerCharacter = 3;
                profile.OffHandWeaponOptionsPerCharacter = 2;
                profile.UltimateOptionsPerCharacter = hasExplicitCoverageRequests ? 2 : 1;
                profile.MainOutfitOptionsPerCharacter = 2;
                profile.SubOutfitOptionsPerCharacter = DefaultSubOutfitOptionsPerCharacter;
                profile.RetainedVariantsPerCharacter = hasExplicitCoverageRequests ? 4 : 3;
                profile.SkeletonExpansionLimit = AdaptiveVeryWideRosterSkeletonExpansionLimit;
                profile.CharacterShortlistLimit = hasExplicitCoverageRequests ? 9 : AdaptiveVeryWideRosterCharacterShortlistLimit;
                return profile;
            }

            if (rosterSize >= AdaptiveWideRosterThreshold)
            {
                profile.MainWeaponOptionsPerCharacter = 3;
                profile.OffHandWeaponOptionsPerCharacter = 2;
                profile.UltimateOptionsPerCharacter = 1;
                profile.MainOutfitOptionsPerCharacter = 2;
                profile.SubOutfitOptionsPerCharacter = DefaultSubOutfitOptionsPerCharacter;
                profile.RetainedVariantsPerCharacter = hasExplicitCoverageRequests ? 5 : 4;
                profile.SkeletonExpansionLimit = AdaptiveWideRosterSkeletonExpansionLimit;
                profile.CharacterShortlistLimit = hasExplicitCoverageRequests ? 11 : AdaptiveWideRosterCharacterShortlistLimit;
            }

            return profile;
        }

        private Dictionary<string, List<CharacterBuildCandidate>> ToVariantDictionary(IReadOnlyDictionary<string, List<CharacterBuildCandidate>> variantsByCharacter)
        {
            return variantsByCharacter.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        }

        private List<TeamCandidate> BuildTeamCandidatesFromSkeletons(
            IReadOnlyDictionary<string, List<CharacterBuildCandidate>> seedVariantsByCharacter,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ownedWeaponsByCharacter,
            IReadOnlyDictionary<string, List<OwnedWeaponCandidate>> ultimateWeaponsByCharacter,
            IReadOnlyDictionary<string, List<OwnedCostumeCandidate>> ownedCostumesByCharacter,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            AdaptiveSearchProfile adaptiveSearchProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            IReadOnlyList<HashSet<string>> mutuallyExclusiveCharacterGroups,
            ConcurrentDictionary<string, SlotEvaluation> weaponSlotEvaluationCache,
            ConcurrentDictionary<string, SlotEvaluation> costumeSlotEvaluationCache)
        {
            var skeletons = BuildTeamSkeletons(seedVariantsByCharacter, request, referenceTuningProfile, adaptiveSearchProfile, normalizedEnabledTemplateNames, mutuallyExclusiveCharacterGroups);
            if (skeletons.Count == 0)
            {
                return new List<TeamCandidate>();
            }

            var teamCandidates = new List<TeamCandidate>();
            foreach (var skeleton in skeletons)
            {
                var variantsByCharacter = new Dictionary<string, List<CharacterBuildCandidate>>(StringComparer.OrdinalIgnoreCase);
                var expansionFailed = false;
                foreach (var seed in skeleton.SeedVariants)
                {
                    var variants = BuildCharacterVariants(
                        seed.CharacterName,
                        ownedWeaponsByCharacter,
                        ultimateWeaponsByCharacter,
                        ownedCostumesByCharacter,
                        request,
                        referenceTuningProfile,
                        adaptiveSearchProfile,
                        weaponSlotEvaluationCache,
                        costumeSlotEvaluationCache,
                        seed.MainWeapon.Name,
                        true);
                    if (variants.Count == 0)
                    {
                        expansionFailed = true;
                        break;
                    }

                    variantsByCharacter[seed.CharacterName] = variants;
                }

                if (expansionFailed)
                {
                    continue;
                }

                var teamCharacters = skeleton.SeedVariants
                    .Select(variant => variant.CharacterName)
                    .OrderBy(character => character, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (var candidate in BuildTeamCandidatesForCharacters(teamCharacters, variantsByCharacter, ownedWeapons, request, referenceTuningProfile, normalizedEnabledTemplateNames, weaponSlotEvaluationCache))
                {
                    candidate.DebugNotes.Insert(0, $"Skeleton score {skeleton.Score:0.##}: anchor {skeleton.AnchorCharacterName} using {skeleton.AnchorWeaponName}.");
                    teamCandidates.Add(candidate);
                }
            }

            return OrderTeamCandidatesForSelection(teamCandidates)
                .Take(Math.Max(1, adaptiveSearchProfile.SkeletonExpansionLimit))
                .ToList();
        }

        private List<TeamSkeleton> BuildTeamSkeletons(
            IReadOnlyDictionary<string, List<CharacterBuildCandidate>> seedVariantsByCharacter,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            AdaptiveSearchProfile adaptiveSearchProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            IReadOnlyList<HashSet<string>> mutuallyExclusiveCharacterGroups)
        {
            var seedPool = seedVariantsByCharacter
                .Values
                .SelectMany(variants => variants)
                .OrderByDescending(variant => GetVariantSelectionScore(variant, request))
                .ThenBy(BuildCharacterVariantEquipmentKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (seedPool.Count < 3)
            {
                return new List<TeamSkeleton>();
            }

            var anchorCandidates = seedPool
                .OrderByDescending(variant => ScoreAnchorCandidate(variant, request))
                .ThenByDescending(variant => GetVariantSelectionScore(variant, request))
                .ThenBy(BuildCharacterVariantEquipmentKey, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(6, adaptiveSearchProfile.SkeletonExpansionLimit))
                .ToList();
            var skeletons = new List<TeamSkeleton>();
            var supportCandidateLimit = Math.Max(6, Math.Min(10, adaptiveSearchProfile.SkeletonExpansionLimit));
            foreach (var anchor in anchorCandidates)
            {
                var rankedSupportCandidates = seedPool
                    .Where(candidate => !candidate.CharacterName.Equals(anchor.CharacterName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(candidate => ScoreSupportSeedForAnchor(anchor, candidate, request))
                    .ThenByDescending(candidate => GetVariantSelectionScore(candidate, request))
                    .ThenBy(BuildCharacterVariantEquipmentKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var supportCandidates = rankedSupportCandidates
                    .Take(supportCandidateLimit)
                    .ToList();
                supportCandidates.AddRange(rankedSupportCandidates
                    .Where(candidate => !supportCandidates.Contains(candidate)
                        && ShouldPreserveContextualSupportSeedVariant(candidate, request))
                    .Take(2));
                for (var i = 0; i < supportCandidates.Count; i++)
                {
                    for (var j = i + 1; j < supportCandidates.Count; j++)
                    {
                        var supportA = supportCandidates[i];
                        var supportB = supportCandidates[j];
                        if (supportA.CharacterName.Equals(supportB.CharacterName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var teamCharacters = new[] { anchor.CharacterName, supportA.CharacterName, supportB.CharacterName };
                        if (!IsCharacterCombinationAllowed(teamCharacters, mutuallyExclusiveCharacterGroups))
                        {
                            continue;
                        }

                        var seedVariants = new[] { anchor, supportA, supportB }
                            .OrderBy(variant => variant.CharacterName, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var skeletonScore = ScoreTeamSkeleton(seedVariants, request, referenceTuningProfile, normalizedEnabledTemplateNames);
                        skeletons.Add(new TeamSkeleton
                        {
                            SeedVariants = seedVariants,
                            Score = skeletonScore,
                            TeamKey = string.Join("|", seedVariants.Select(variant => variant.CharacterName)),
                            EquipmentKey = string.Join("|", seedVariants.Select(BuildCharacterVariantEquipmentKey)),
                            AnchorCharacterName = anchor.CharacterName,
                            AnchorWeaponName = anchor.MainWeapon.Name
                        });
                    }
                }
            }

            return skeletons
                .OrderByDescending(skeleton => skeleton.Score)
                .ThenBy(skeleton => skeleton.TeamKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(skeleton => skeleton.EquipmentKey, StringComparer.OrdinalIgnoreCase)
                .DistinctBy(skeleton => skeleton.EquipmentKey, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, adaptiveSearchProfile.SkeletonExpansionLimit))
                .ToList();
        }

        private double ScoreTeamSkeleton(
            IReadOnlyList<CharacterBuildCandidate> seedVariants,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames)
        {
            var providedEffectKeys = GetProvidedEffectKeys(seedVariants);

            double score;
            if (request.PreferredDamageType != DamageType.Any)
            {
                // Target #2: rank skeletons by the REAL multiplicative team-damage model instead of the legacy
                // parallel hand-tuned estimator (Σ GetVariantSelectionScore + effect-package + pyramid-coverage +
                // offensive-shell + synergy proxies). Meaningful now that seeds carry their ultimate (target #1).
                // Keep only the structural refinements — user requirement coverage + template-role gating — at the
                // same weights, mirroring FinalizeTeamCandidate so the skeleton GATE and the final ranking agree.
                score = EstimateTeamDamage(seedVariants, request);
                score += PreferredCoverageBonus(providedEffectKeys, request);
                score += request.HardRequiredEffectKeys.Count(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) * 70d;
                score += request.SoftPreferredEffectKeys.Count(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) * 45d;
            }
            else
            {
                // No damage axis ("Any"): the multiplicative model has nothing to gate on, so keep the legacy
                // heuristic backbone for this case.
                var explicitDetectedEffects = seedVariants.SelectMany(variant => GetDetectedEffectsForVariant(variant, request)).ToList();
                var effectPackage = ScoreEffectPackage(explicitDetectedEffects, seedVariants, request);
                var anchorSupportScore = ScoreAnchorSupportSynergy(seedVariants, explicitDetectedEffects, request);
                var contextualVariantBonus = GetVariantContextualTeamBonus(seedVariants, request);
                var offensiveShellScore = ScoreOffensiveShell(seedVariants, request);
                score = seedVariants.Sum(variant => GetVariantSelectionScore(variant, request));
                score += effectPackage.Score;
                score += anchorSupportScore.Score;
                score += contextualVariantBonus;
                score += offensiveShellScore.Score * 0.45;
                score += ScoreTeamEffects(providedEffectKeys, request) * 0.12;
                score += PreferredCoverageBonus(providedEffectKeys, request);
                score += ScorePyramidCoverage(providedEffectKeys, request);
                score += request.HardRequiredEffectKeys.Count(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) * 70d;
                score += request.SoftPreferredEffectKeys.Count(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) * 45d;
                score += ScoreReferencePatternSynergyBonus(seedVariants, providedEffectKeys, request, referenceTuningProfile) * 0.4;
            }

            var roles = seedVariants
                .Select(variant => variant.Role.ToString())
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var teamRoleKey = NormalizeTemplateName(string.Join("/", roles));
            if (!normalizedEnabledTemplateNames.ContainsKey(teamRoleKey))
            {
                score *= 0.7;
            }

            return score;
        }

        private double ScoreSupportSeedForAnchor(
            CharacterBuildCandidate anchor,
            CharacterBuildCandidate support,
            PlayerPowerAnalyzerV2Request request)
        {
            var variants = new[] { anchor, support };
            var providedEffectKeys = GetProvidedEffectKeys(variants);
            var explicitDetectedEffects = variants.SelectMany(variant => GetDetectedEffectsForVariant(variant, request)).ToList();
            var anchorSupportScore = ScoreAnchorSupportSynergy(variants, explicitDetectedEffects, request).Score;
            var score = GetVariantSelectionScore(support, request) * 0.85;
            score += anchorSupportScore;
            score += GetVariantContextualTeamBonus(support, request);
            score += GetVariantOffensiveRoleSelectionBonus(support, request) * 0.25;
            score -= GetVariantLowActualUsePenalty(support, request) * 0.15;
            score += PreferredCoverageBonus(providedEffectKeys, request) * 0.45;
            score += ScorePyramidCoverage(providedEffectKeys, request) * 0.35;
            return score;
        }

        private static double ScorePyramidCoverage(IReadOnlyCollection<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request)
        {
            if (providedEffectKeys.Count == 0)
            {
                return 0d;
            }

            var foundationKeys = request.PreferredDamageType == DamageType.Magical
                ? new[] { "matk_up", "mdef_down" }
                : request.PreferredDamageType == DamageType.Physical
                    ? new[] { "patk_up", "pdef_down" }
                    : new[] { "patk_up", "matk_up", "pdef_down", "mdef_down" };
            var enhancementKeys = new[] { "elemental_resistance_down" }
                .Concat(GetRelevantEnhancementKeys(request))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var multiplierKeys = request.PreferredDamageType == DamageType.Magical
                ? new[] { "mag_damage_bonus", "mag_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" }
                : request.PreferredDamageType == DamageType.Physical
                    ? new[] { "phys_damage_bonus", "phys_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" }
                    : new[] { "phys_damage_bonus", "phys_weapon_boost", "mag_damage_bonus", "mag_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" };
            var amplifierKeys = new[] { "stat_buff_tier_increase", "stat_debuff_tier_increase", "exploit_weakness", "enfeeble", "enliven", "torpor", "atb_conservation", "atb_gain" };

            var score = 0d;
            if (foundationKeys.Any(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                score += 120d;
            }

            if (enhancementKeys.Any(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                score += 82d;
            }

            if (multiplierKeys.Any(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                score += 68d;
            }

            if (amplifierKeys.Any(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                score += 44d;
            }

            return score;
        }

        private AdaptiveCharacterShortlistResult ApplyAdaptiveCharacterShortlist(
            IReadOnlyDictionary<string, List<CharacterBuildCandidate>> variantsByCharacter,
            PlayerPowerAnalyzerV2Request request,
            AdaptiveSearchProfile adaptiveSearchProfile)
        {
            var populatedCandidates = variantsByCharacter
                .Where(entry => entry.Value.Count > 0)
                .Select(entry => new AdaptiveCharacterCandidate
                {
                    CharacterName = entry.Key,
                    Role = entry.Value[0].Role,
                    BestVariantSelectionScore = GetVariantSelectionScore(entry.Value[0], request),
                    ProvidedEffectKeys = new HashSet<string>(entry.Value.SelectMany(variant => variant.ProvidedEffectKeys), StringComparer.OrdinalIgnoreCase)
                })
                .OrderByDescending(candidate => candidate.BestVariantSelectionScore)
                .ThenBy(candidate => candidate.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (adaptiveSearchProfile.CharacterShortlistLimit == int.MaxValue
                || populatedCandidates.Count <= Math.Max(3, adaptiveSearchProfile.CharacterShortlistLimit))
            {
                return new AdaptiveCharacterShortlistResult
                {
                    VariantsByCharacter = ToVariantDictionary(variantsByCharacter),
                    OriginalCharacterCount = populatedCandidates.Count,
                    RetainedCharacterCount = populatedCandidates.Count
                };
            }

            var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderedSelectedNames = new List<string>();

            void AddCandidate(AdaptiveCharacterCandidate? candidate)
            {
                if (candidate == null || !selectedNames.Add(candidate.CharacterName))
                {
                    return;
                }

                orderedSelectedNames.Add(candidate.CharacterName);
            }

            foreach (var candidate in populatedCandidates.Where(candidate => candidate.Role == CharacterRole.DPS).Take(AdaptiveShortlistMinimumDpsCount))
            {
                AddCandidate(candidate);
            }

            foreach (var candidate in populatedCandidates.Where(candidate => candidate.Role == CharacterRole.Healer).Take(AdaptiveShortlistMinimumHealerCount))
            {
                AddCandidate(candidate);
            }

            foreach (var candidate in populatedCandidates.Where(candidate => candidate.Role == CharacterRole.Support).Take(AdaptiveShortlistMinimumSupportCount))
            {
                AddCandidate(candidate);
            }

            foreach (var candidate in populatedCandidates.Where(candidate => candidate.Role == CharacterRole.Tank).Take(AdaptiveShortlistMinimumTankCount))
            {
                AddCandidate(candidate);
            }

            foreach (var effectKey in request.HardRequiredEffectKeys.Concat(request.SoftPreferredEffectKeys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddCandidate(populatedCandidates.FirstOrDefault(candidate => candidate.ProvidedEffectKeys.Contains(effectKey)));
            }

            foreach (var candidate in populatedCandidates)
            {
                if (orderedSelectedNames.Count >= adaptiveSearchProfile.CharacterShortlistLimit)
                {
                    break;
                }

                AddCandidate(candidate);
            }

            foreach (var candidate in populatedCandidates)
            {
                if (orderedSelectedNames.Count >= 3)
                {
                    break;
                }

                AddCandidate(candidate);
            }

            var shortlistedVariants = orderedSelectedNames
                .ToDictionary(characterName => characterName, characterName => variantsByCharacter[characterName], StringComparer.OrdinalIgnoreCase);

            return new AdaptiveCharacterShortlistResult
            {
                VariantsByCharacter = shortlistedVariants,
                OriginalCharacterCount = populatedCandidates.Count,
                RetainedCharacterCount = shortlistedVariants.Count,
                WasShortlisted = shortlistedVariants.Count < populatedCandidates.Count
            };
        }

        private static double GetVariantSelectionScore(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var providedKeys = new HashSet<string>(variant.ProvidedEffectKeys, StringComparer.OrdinalIgnoreCase);
            var detectedEffects = GetDetectedEffectsForVariant(variant, request);
            var bonus = PreferredCoverageBonus(providedKeys, request);
            bonus += GetAnchorCandidateSelectionBonus(variant, request);
            bonus += GetVariantWeaponOrientationSelectionBonus(variant, request);
            bonus += GetVariantGearCAbilityUsesSelectionBonus(variant, request);
            bonus += GetVariantRequestedAxisMultiplierSelectionBonus(variant, request);
            bonus += GetVariantRequestedElementCarrySelectionBonus(variant, request);
            bonus += GetVariantOffensiveRoleSelectionBonus(variant, request) * 0.2;
            bonus -= GetVariantLowActualUsePenalty(variant, request) * 0.12;

            if (providedKeys.Count == 0)
            {
                return variant.BaseScore + bonus;
            }

            if (providedKeys.Contains("stat_buff_tier_increase", StringComparer.OrdinalIgnoreCase)
                && providedKeys.Any(key => key is "patk_up" or "matk_up"))
            {
                bonus += request.PreferredDamageType == DamageType.Magical ? 115 : 135;
            }
            else if (providedKeys.Contains("stat_buff_tier_increase", StringComparer.OrdinalIgnoreCase)
                && providedKeys.Any(key => key is "pdef_up" or "mdef_up"))
            {
                bonus += request.PreferredDamageType == DamageType.Magical ? 38 : 42;
            }

            if (providedKeys.Contains("stat_debuff_tier_increase", StringComparer.OrdinalIgnoreCase)
                && providedKeys.Any(key => key is "patk_down" or "matk_down" or "pdef_down" or "mdef_down" or "elemental_resistance_down"))
            {
                bonus += request.PreferredDamageType == DamageType.Magical ? 120 : 130;
            }

            if (variant.SelectionScoreOverride.HasValue)
            {
                return variant.SelectionScoreOverride.Value;
            }

            bonus -= GetVariantOffAxisPackagePenalty(variant, providedKeys, detectedEffects, request);
            bonus += (ScoreEffectPackage(detectedEffects, Array.Empty<CharacterBuildCandidate>(), request).Score * 0.09)
                + (ScoreTeamEffects(providedKeys, request) * 0.03);
            var selectionScore = variant.BaseScore + bonus;
            variant.SelectionScoreOverride = selectionScore;
            return selectionScore;
        }

        private static double GetVariantOffAxisPackagePenalty(
            CharacterBuildCandidate variant,
            IReadOnlyCollection<string> providedEffectKeys,
            IReadOnlyCollection<DetectedActiveEffect> detectedEffects,
            PlayerPowerAnalyzerV2Request request)
        {
            if (providedEffectKeys.Count == 0)
            {
                return 0d;
            }

            var penalty = 0d;
            var baseRole = CharacterRoleRegistry.GetRoleOrDefault(variant.CharacterName);
            var treatAsNonDps = variant.Role != CharacterRole.DPS || baseRole != CharacterRole.DPS;
            var matchingRequestedElementResDownCount = request.EnemyWeakness == Element.None
                ? 0
                : detectedEffects
                    .Select(GetDetectedElementalResistanceDownTargetElement)
                    .Where(targetElement => !string.IsNullOrWhiteSpace(targetElement)
                        && targetElement.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            var offAxisElementResDownCount = request.EnemyWeakness == Element.None
                ? 0
                : detectedEffects
                    .Select(GetDetectedElementalResistanceDownTargetElement)
                    .Where(targetElement => !string.IsNullOrWhiteSpace(targetElement)
                        && !targetElement.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            var hasRequestedElementAxis = request.EnemyWeakness != Element.None
                && HasRequestedElementMainOrOffHand(variant, request);
            var hasExplicitOffAxisElement = request.EnemyWeakness != Element.None
                && new[] { variant.MainWeapon, variant.OffHandWeapon }
                    .Where(slot => slot != null)
                    .Any(slot => !string.IsNullOrWhiteSpace(slot!.Element)
                        && !slot.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                        && !MatchesRequestedElement(slot.Element, request.EnemyWeakness));
            if (request.EnemyWeakness != Element.None
                && providedEffectKeys.Contains("elemental_resistance_down", StringComparer.OrdinalIgnoreCase)
                && matchingRequestedElementResDownCount == 0)
            {
                penalty += treatAsNonDps ? 220d : 64d;
                if (offAxisElementResDownCount > 1)
                {
                    penalty += (offAxisElementResDownCount - 1) * (treatAsNonDps ? 118d : 34d);
                }
            }

            if (variant.Role == CharacterRole.DPS
                && request.EnemyWeakness != Element.None
                && !hasRequestedElementAxis)
            {
                var offensiveSetupKeyCount = providedEffectKeys.Count(key => key is
                    "patk_up"
                    or "matk_up"
                    or "pdef_down"
                    or "mdef_down"
                    or "elemental_damage_up"
                    or "elemental_damage_received_up"
                    or "phys_damage_bonus"
                    or "mag_damage_bonus"
                    or "phys_weapon_boost"
                    or "mag_weapon_boost"
                    or "elemental_weapon_boost"
                    or "exploit_weakness"
                    or "gear_c_ability_uses");
                if (offensiveSetupKeyCount > 0)
                {
                    penalty += hasExplicitOffAxisElement
                        ? 86d + ((offensiveSetupKeyCount - 1) * 18d)
                        : 52d + ((offensiveSetupKeyCount - 1) * 12d);
                }
            }

            if (!treatAsNonDps || request.PreferredDamageType == DamageType.Any)
            {
                return penalty;
            }

            var hasRelevantDamageBonus = request.PreferredDamageType switch
            {
                DamageType.Physical => providedEffectKeys.Any(key => key is "phys_damage_bonus" or "phys_weapon_boost" or "elemental_damage_bonus" or "elemental_weapon_boost"),
                DamageType.Magical => providedEffectKeys.Any(key => key is "mag_damage_bonus" or "mag_weapon_boost" or "elemental_damage_bonus" or "elemental_weapon_boost"),
                _ => false
            };
            var hasOffAxisDamageBonus = request.PreferredDamageType switch
            {
                DamageType.Physical => providedEffectKeys.Any(key => key is "mag_damage_bonus" or "mag_weapon_boost"),
                DamageType.Magical => providedEffectKeys.Any(key => key is "phys_damage_bonus" or "phys_weapon_boost"),
                _ => false
            };
            var hasRelevantFoundation = request.PreferredDamageType switch
            {
                DamageType.Physical => providedEffectKeys.Any(key => key is "patk_up" or "pdef_down" or "elemental_resistance_down"),
                DamageType.Magical => providedEffectKeys.Any(key => key is "matk_up" or "mdef_down" or "elemental_resistance_down"),
                _ => false
            };

            if (hasOffAxisDamageBonus && !hasRelevantDamageBonus)
            {
                penalty += hasRelevantFoundation ? 170d : 240d;
            }

            return penalty;
        }

        private static double GetVariantGearCAbilityUsesSelectionBonus(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (variant.Role is not CharacterRole.Healer and not CharacterRole.Support
                || variant.MainOutfit == null
                || !TryParseLimitedUseCount(variant.MainOutfit.UseCount, out _))
            {
                return 0d;
            }

            var gearCProvider = new[] { variant.MainWeapon, variant.OffHandWeapon }
                .Where(slot => slot != null)
                .FirstOrDefault(slot => slot!.ProvidedEffectLabels.Contains("Gear C. Ability Uses", StringComparer.OrdinalIgnoreCase));
            if (gearCProvider == null)
            {
                return 0d;
            }

            var outfitEffectKeys = DetectEffectKeys(Array.Empty<string>(), variant.MainOutfit.AbilityText, request, request.BossImmunityKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outfitAbilityMatchesRequest = request.PreferredDamageType == DamageType.Any
                || string.IsNullOrWhiteSpace(variant.MainOutfit.AbilityType)
                || MatchesRequestedDamageType(variant.MainOutfit.AbilityType, request.PreferredDamageType);
            var outfitIsRelevantLimitedUseAbility = outfitEffectKeys.Any(IsOffensiveSetupEffect)
                || outfitEffectKeys.Any(key => key is "stat_buff_tier_increase" or "stat_debuff_tier_increase" or "atb_conservation" or "atb_gain" or "enfeeble" or "enliven" or "torpor");
            var providerAddsOnAxisDamageBonus = request.PreferredDamageType switch
            {
                DamageType.Physical => gearCProvider.ProvidedEffectLabels.Contains("Phys. Damage Bonus", StringComparer.OrdinalIgnoreCase),
                DamageType.Magical => gearCProvider.ProvidedEffectLabels.Contains("Mag. Damage Bonus", StringComparer.OrdinalIgnoreCase),
                _ => gearCProvider.ProvidedEffectLabels.Contains("Phys. Damage Bonus", StringComparer.OrdinalIgnoreCase)
                    || gearCProvider.ProvidedEffectLabels.Contains("Mag. Damage Bonus", StringComparer.OrdinalIgnoreCase)
            };

            var bonus = 28d;
            if (outfitAbilityMatchesRequest)
            {
                bonus += 12d;
            }

            if (outfitIsRelevantLimitedUseAbility)
            {
                bonus += 20d;
            }

            if (providerAddsOnAxisDamageBonus)
            {
                bonus += 16d;
            }

            return bonus;
        }

        private static double GetVariantRequestedAxisMultiplierSelectionBonus(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (variant.Role is not CharacterRole.Healer and not CharacterRole.Support)
            {
                return 0d;
            }

            var providedKeys = variant.ProvidedEffectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relevantMultiplierKeys = GetRelevantVariantMultiplierKeys(request);
            var relevantMultiplierCount = relevantMultiplierKeys.Count(key => providedKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
            if (relevantMultiplierCount == 0)
            {
                return 0d;
            }

            var hasRequestedFoundation = request.PreferredDamageType switch
            {
                DamageType.Physical => providedKeys.Contains("patk_up") || providedKeys.Contains("pdef_down") || providedKeys.Contains("elemental_resistance_down"),
                DamageType.Magical => providedKeys.Contains("matk_up") || providedKeys.Contains("mdef_down") || providedKeys.Contains("elemental_resistance_down"),
                _ => providedKeys.Contains("patk_up") || providedKeys.Contains("matk_up") || providedKeys.Contains("pdef_down") || providedKeys.Contains("mdef_down") || providedKeys.Contains("elemental_resistance_down")
            };

            var bonus = 24d + (relevantMultiplierCount * 18d);
            if (providedKeys.Contains("gear_c_ability_uses"))
            {
                bonus += 16d;
            }

            if (hasRequestedFoundation)
            {
                bonus += 14d;
            }

            return Math.Min(78d, bonus);
        }

        private static double GetVariantRequestedElementCarrySelectionBonus(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (request.EnemyWeakness == Element.None
                || variant.Role != CharacterRole.DPS
                || !HasRequestedElementMainOrOffHand(variant, request))
            {
                return 0d;
            }

            var inference = EnsureOffensiveRoleInference(variant, request);
            return inference.Role switch
            {
                OffensiveRoleProfile.Carry => Math.Min(82d, 34d + (inference.DirectPressure * 0.065)),
                OffensiveRoleProfile.OffensiveEnabler => Math.Min(34d, 10d + (inference.DirectPressure * 0.03)),
                _ => 0d
            };
        }

        private static double GetVariantContextualTeamBonus(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            return GetVariantGearCAbilityUsesSelectionBonus(variant, request)
                + GetVariantRequestedAxisMultiplierSelectionBonus(variant, request) * 0.55;
        }

        private static double GetVariantContextualTeamBonus(IEnumerable<CharacterBuildCandidate> variants, PlayerPowerAnalyzerV2Request request)
        {
            return variants.Sum(variant => GetVariantContextualTeamBonus(variant, request));
        }

        private static OffensiveRoleInferenceResult EnsureOffensiveRoleInference(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (!string.IsNullOrWhiteSpace(variant.OffensiveRoleReason))
            {
                return new OffensiveRoleInferenceResult
                {
                    Role = variant.OffensiveRole,
                    Reason = variant.OffensiveRoleReason,
                    DirectPressure = variant.EstimatedDirectOffenseScore,
                    EnablerPressure = variant.EstimatedOffensiveSupportScore,
                    LowActualUsePenalty = variant.EstimatedLowActualUsePenalty
                };
            }

            var inference = InferOffensiveRoleProfile(variant, request);
            variant.OffensiveRole = inference.Role;
            variant.OffensiveRoleReason = inference.Reason;
            variant.EstimatedDirectOffenseScore = inference.DirectPressure;
            variant.EstimatedOffensiveSupportScore = inference.EnablerPressure;
            variant.EstimatedLowActualUsePenalty = inference.LowActualUsePenalty;
            return inference;
        }

        private static OffensiveRoleInferenceResult InferOffensiveRoleProfile(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var detectedEffects = GetDetectedEffectsForVariant(variant, request);
            var activeSourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                variant.MainWeapon.Name
            };
            if (variant.OffHandWeapon != null)
            {
                activeSourceNames.Add(variant.OffHandWeapon.Name);
            }

            if (variant.UltimateWeapon != null)
            {
                activeSourceNames.Add(variant.UltimateWeapon.Name);
            }

            if (variant.MainOutfit != null)
            {
                activeSourceNames.Add(variant.MainOutfit.Name);
            }

            var directPressure = ScoreVariantDirectPressure(variant, request);
            var enablerPressure = detectedEffects.Sum(effect => ScoreOffensiveEnablerEffect(effect, request));
            var sustainPressure = ScoreVariantSustainPressure(variant, detectedEffects);
            var defensivePressure = ScoreVariantDefensivePressure(variant, detectedEffects);
            var activeSetupCount = detectedEffects
                .Where(effect => activeSourceNames.Contains(effect.SourceName))
                .Count(effect => ScoreOffensiveEnablerEffect(effect, request) >= 28d);
            if (activeSetupCount >= 2 && enablerPressure > 0)
            {
                directPressure *= 0.82;
            }

            OffensiveRoleProfile role;
            if (activeSetupCount >= 2
                && enablerPressure >= 190d
                && directPressure <= enablerPressure * 1.55)
            {
                role = OffensiveRoleProfile.OffensiveEnabler;
            }
            else if (directPressure >= Math.Max(340d, enablerPressure * 1.18)
                && directPressure >= sustainPressure * 1.35)
            {
                role = OffensiveRoleProfile.Carry;
            }
            else if (variant.Role == CharacterRole.DPS
                && HasRequestedElementMainOrOffHand(variant, request)
                && directPressure >= Math.Max(280d, enablerPressure * 0.82)
                && directPressure >= sustainPressure * 1.2)
            {
                role = OffensiveRoleProfile.Carry;
            }
            else if (enablerPressure >= Math.Max(175d, directPressure * 0.72)
                && enablerPressure >= defensivePressure * 1.05)
            {
                role = OffensiveRoleProfile.OffensiveEnabler;
            }
            else if (sustainPressure >= Math.Max(155d, enablerPressure * 0.95))
            {
                role = OffensiveRoleProfile.SustainSupport;
            }
            else
            {
                role = OffensiveRoleProfile.DefensiveSupport;
            }

            if (role == OffensiveRoleProfile.Carry
                && variant.Role is CharacterRole.Healer or CharacterRole.Support or CharacterRole.Tank
                && enablerPressure > 0)
            {
                role = OffensiveRoleProfile.OffensiveEnabler;
            }

            var lowActualUsePenalty = 0d;
            if (role == OffensiveRoleProfile.OffensiveEnabler && directPressure < 280d && enablerPressure < 210d)
            {
                lowActualUsePenalty += 32d;
            }

            if (role == OffensiveRoleProfile.SustainSupport && request.PreferredDamageType != DamageType.Any)
            {
                lowActualUsePenalty += 58d;
            }

            if (role == OffensiveRoleProfile.DefensiveSupport && request.PreferredDamageType != DamageType.Any)
            {
                lowActualUsePenalty += 76d;
            }

            if (activeSetupCount >= 2 && directPressure < 240d)
            {
                lowActualUsePenalty += 24d;
            }

            if (role != OffensiveRoleProfile.Carry && enablerPressure < 110d && directPressure < 210d)
            {
                lowActualUsePenalty += 36d;
            }

            return new OffensiveRoleInferenceResult
            {
                Role = role,
                DirectPressure = Math.Round(directPressure, 2),
                EnablerPressure = Math.Round(enablerPressure, 2),
                LowActualUsePenalty = Math.Round(lowActualUsePenalty, 2),
                Reason = $"direct={directPressure:0.##}, enabler={enablerPressure:0.##}, sustain={sustainPressure:0.##}, defense={defensivePressure:0.##}, activeSetup={activeSetupCount}"
            };
        }

        private static double GetVariantOffensiveRoleSelectionBonus(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var inference = EnsureOffensiveRoleInference(variant, request);
            return inference.Role switch
            {
                OffensiveRoleProfile.Carry => Math.Min(72d, 24d + (inference.DirectPressure * 0.08)),
                OffensiveRoleProfile.OffensiveEnabler => Math.Min(62d, 18d + (inference.EnablerPressure * 0.12)),
                OffensiveRoleProfile.SustainSupport => request.PreferredDamageType == DamageType.Any ? -4d : -18d,
                OffensiveRoleProfile.DefensiveSupport => request.PreferredDamageType == DamageType.Any ? -8d : -28d,
                _ => 0d
            };
        }

        private static double GetVariantLowActualUsePenalty(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            return EnsureOffensiveRoleInference(variant, request).LowActualUsePenalty;
        }

        private static double ScoreVariantDirectPressure(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var score = ScoreVariantOffensiveSlotPressure(variant.MainWeapon, request, 0.38);
            score += ScoreVariantOffensiveSlotPressure(variant.OffHandWeapon, request, 0.26);
            score += ScoreVariantOffensiveSlotPressure(variant.UltimateWeapon, request, 0.16);
            score += request.PreferredDamageType switch
            {
                DamageType.Physical => (variant.MainWeapon.Patk + (variant.OffHandWeapon?.Patk ?? 0) + (variant.UltimateWeapon?.Patk ?? 0)) * 0.16,
                DamageType.Magical => (variant.MainWeapon.Matk + (variant.OffHandWeapon?.Matk ?? 0) + (variant.UltimateWeapon?.Matk ?? 0)) * 0.16,
                _ => Math.Max(
                    variant.MainWeapon.Patk + (variant.OffHandWeapon?.Patk ?? 0) + (variant.UltimateWeapon?.Patk ?? 0),
                    variant.MainWeapon.Matk + (variant.OffHandWeapon?.Matk ?? 0) + (variant.UltimateWeapon?.Matk ?? 0)) * 0.16
            };
            if (variant.Role == CharacterRole.DPS)
            {
                score *= 1.08;
            }

            return score;
        }

        private static double ScoreVariantOffensiveSlotPressure(PlayerPowerAnalyzerV2ItemSlot? slot, PlayerPowerAnalyzerV2Request request, double damageWeight)
        {
            if (slot == null)
            {
                return 0d;
            }

            var damageScore = Math.Max(0d, slot.DamagePercent) * damageWeight;
            var typeMultiplier = request.PreferredDamageType == DamageType.Any || MatchesRequestedDamageType(slot.AbilityType, request.PreferredDamageType)
                ? 1.0
                : 0.55;
            var elementMultiplier = request.EnemyWeakness == Element.None
                || string.IsNullOrWhiteSpace(slot.Element)
                || slot.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                || MatchesRequestedElement(slot.Element, request.EnemyWeakness)
                ? 1.0
                : 0.7;
            var statScore = request.PreferredDamageType switch
            {
                DamageType.Physical => slot.Patk * 0.12,
                DamageType.Magical => slot.Matk * 0.12,
                _ => Math.Max(slot.Patk, slot.Matk) * 0.12
            };
            return (damageScore * typeMultiplier * elementMultiplier) + statScore;
        }

        private static double ScoreOffensiveEnablerEffect(DetectedActiveEffect effect, PlayerPowerAnalyzerV2Request request)
        {
            var baseWeight = effect.Key switch
            {
                "elemental_resistance_down" => 135d,
                "elemental_damage_received_up" => 112d,
                "elemental_damage_up" => 94d,
                "elemental_damage_bonus" or "elemental_weapon_boost" => 92d,
                "phys_damage_received_up" => request.PreferredDamageType == DamageType.Physical ? 118d : 40d,
                "mag_damage_received_up" => request.PreferredDamageType == DamageType.Magical ? 118d : 40d,
                "phys_damage_bonus" or "phys_weapon_boost" => request.PreferredDamageType == DamageType.Physical ? 94d : 34d,
                "mag_damage_bonus" or "mag_weapon_boost" => request.PreferredDamageType == DamageType.Magical ? 94d : 34d,
                "patk_up" => request.PreferredDamageType == DamageType.Physical ? 104d : 42d,
                "matk_up" => request.PreferredDamageType == DamageType.Magical ? 104d : 42d,
                "pdef_down" => request.PreferredDamageType == DamageType.Physical ? 108d : 48d,
                "mdef_down" => request.PreferredDamageType == DamageType.Magical ? 108d : 48d,
                "stat_buff_tier_increase" => 84d,
                "stat_debuff_tier_increase" => 88d,
                "atb_gain" => 68d,
                "atb_conservation" => 56d,
                "exploit_weakness" => 74d,
                "enfeeble" => 68d,
                "enliven" => 58d,
                "torpor" => 52d,
                _ => 0d
            };
            if (baseWeight <= 0.001)
            {
                return 0d;
            }

            if (effect.Key.Equals("elemental_resistance_down", StringComparison.OrdinalIgnoreCase)
                && request.EnemyWeakness != Element.None
                && !IsRequestedElementalResistanceDownEffect(effect, request))
            {
                baseWeight *= 0.35;
            }

            var scopeMultiplier = effect.TargetScope switch
            {
                ActiveEffectTargetScope.AllAllies => 1.1,
                ActiveEffectTargetScope.OtherAllies => 1.04,
                ActiveEffectTargetScope.SingleAlly => 0.9,
                ActiveEffectTargetScope.SingleEnemy => 1.0,
                ActiveEffectTargetScope.AllEnemies => 1.06,
                ActiveEffectTargetScope.Self => 0.25,
                _ => 0.55
            };
            return baseWeight * scopeMultiplier;
        }

        private static double ScoreVariantSustainPressure(CharacterBuildCandidate variant, IReadOnlyCollection<DetectedActiveEffect> detectedEffects)
        {
            var healScore = (variant.MainWeapon.Heal + (variant.OffHandWeapon?.Heal ?? 0) + (variant.UltimateWeapon?.Heal ?? 0)) * 0.18;
            var effectScore = detectedEffects.Sum(effect => effect.Key switch
            {
                "healing_support" => effect.TargetScope == ActiveEffectTargetScope.AllAllies ? 112d : 84d,
                "pdef_up" or "mdef_up" => effect.TargetScope == ActiveEffectTargetScope.AllAllies ? 38d : 24d,
                _ => 0d
            });
            return healScore + effectScore;
        }

        private static double ScoreVariantDefensivePressure(CharacterBuildCandidate variant, IReadOnlyCollection<DetectedActiveEffect> detectedEffects)
        {
            var defensiveEffectScore = detectedEffects.Sum(effect => effect.Key switch
            {
                "pdef_up" or "mdef_up" => effect.TargetScope == ActiveEffectTargetScope.AllAllies ? 52d : 34d,
                "healing_support" => 46d,
                _ => 0d
            });
            var passiveDefenseScore = variant.PassivePoints
                .Where(kvp => kvp.Value > 0
                    && (kvp.Key.Contains("pdef", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("mdef", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("hp", StringComparison.OrdinalIgnoreCase)))
                .Sum(kvp => kvp.Value * 0.9);
            return defensiveEffectScore + passiveDefenseScore;
        }

        private static bool HasStrongOffensiveEnablerMain(PlayerPowerAnalyzerV2ItemSlot slot, PlayerPowerAnalyzerV2Request request)
        {
            var effects = GetDetectedEffectsForSlot(slot, request);
            var providedKeys = effects
                .Select(effect => effect.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return (effects.Sum(effect => ScoreOffensiveEnablerEffect(effect, request)) >= 125d
                    && effects.Any(effect => ScoreOffensiveEnablerEffect(effect, request) >= 34d))
                || HasStrongOffensiveSupportKeys(providedKeys, request);
        }

        private static bool HasStrongOffensiveSupportKeys(IReadOnlyCollection<string> providedKeys, PlayerPowerAnalyzerV2Request request)
        {
            if (providedKeys.Count == 0)
            {
                return false;
            }

            var offensiveSetupKeyCount = providedKeys.Count(key => key is
                "patk_up"
                or "matk_up"
                or "pdef_down"
                or "mdef_down"
                or "elemental_resistance_down"
                or "elemental_damage_up"
                or "elemental_damage_received_up"
                or "phys_damage_bonus"
                or "mag_damage_bonus"
                or "phys_damage_received_up"
                or "mag_damage_received_up"
                or "phys_weapon_boost"
                or "mag_weapon_boost"
                or "elemental_weapon_boost"
                or "exploit_weakness"
                or "stat_buff_tier_increase"
                or "stat_debuff_tier_increase"
                or "gear_c_ability_uses");
            if (offensiveSetupKeyCount >= 2)
            {
                return true;
            }

            var relevantMultiplierKeys = GetRelevantVariantMultiplierKeys(request);
            if (relevantMultiplierKeys.Any(key => providedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                && providedKeys.Any(key => key is "patk_up" or "matk_up" or "pdef_down" or "mdef_down" or "elemental_resistance_down"))
            {
                return true;
            }

            return providedKeys.Contains("gear_c_ability_uses", StringComparer.OrdinalIgnoreCase)
                && relevantMultiplierKeys.Any(key => providedKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
        }

        private static OffensiveShellScoreResult ScoreOffensiveShell(IReadOnlyList<CharacterBuildCandidate> variants, PlayerPowerAnalyzerV2Request request)
        {
            var result = new OffensiveShellScoreResult();
            if (variants.Count == 0)
            {
                return result;
            }

            var inferredRoles = variants
                .Select(variant => (Variant: variant, Inference: EnsureOffensiveRoleInference(variant, request)))
                .ToList();
            var carryCount = inferredRoles.Count(entry => entry.Inference.Role == OffensiveRoleProfile.Carry);
            var enablerCount = inferredRoles.Count(entry => entry.Inference.Role == OffensiveRoleProfile.OffensiveEnabler);
            var sustainCount = inferredRoles.Count(entry => entry.Inference.Role == OffensiveRoleProfile.SustainSupport || entry.Inference.Role == OffensiveRoleProfile.DefensiveSupport);
            var dpsCount = inferredRoles.Count(entry => entry.Variant.Role == CharacterRole.DPS);
            var actualHealerCount = inferredRoles.Count(entry => entry.Variant.Role == CharacterRole.Healer);
            var healerOffensiveUtilityCount = inferredRoles.Count(entry => entry.Variant.Role == CharacterRole.Healer
                && entry.Inference.Role == OffensiveRoleProfile.OffensiveEnabler);
            var healerRequestedMultiplierCount = inferredRoles.Count(entry => entry.Variant.Role == CharacterRole.Healer
                && GetRelevantVariantMultiplierKeys(request).Any(key => entry.Variant.ProvidedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)));
            var requestedElementDpsCount = request.EnemyWeakness == Element.None
                ? dpsCount
                : inferredRoles.Count(entry => entry.Variant.Role == CharacterRole.DPS && HasRequestedElementMainOrOffHand(entry.Variant, request));
            var requestedElementCarryCount = request.EnemyWeakness == Element.None
                ? 0
                : inferredRoles.Count(entry => entry.Inference.Role == OffensiveRoleProfile.Carry && HasRequestedElementMainOrOffHand(entry.Variant, request));
            var supportOnlyEnablerCount = inferredRoles.Count(entry => entry.Variant.Role == CharacterRole.Support
                && entry.Inference.Role == OffensiveRoleProfile.OffensiveEnabler);
            var enabledTemplatesIncludeHealer = request.EnabledTeamTemplates.Any(name => name.Contains("Healer", StringComparison.OrdinalIgnoreCase));
            var topCarryPressure = inferredRoles
                .Where(entry => entry.Inference.Role == OffensiveRoleProfile.Carry)
                .Select(entry => entry.Inference.DirectPressure)
                .DefaultIfEmpty(0d)
                .Max();
            var lowUsePenalty = inferredRoles.Sum(entry => entry.Inference.LowActualUsePenalty);

            if (carryCount > 0)
            {
                result.Score += 110d;
            }
            else
            {
                result.Score -= 180d;
            }

            if (carryCount >= 2)
            {
                result.Score += 95d;
            }

            if (carryCount >= 1 && enablerCount >= 1)
            {
                result.Score += 54d;
            }

            if (dpsCount >= 2)
            {
                result.Score += 88d;
            }

            if (carryCount == 1 && enablerCount >= 1 && sustainCount == 0)
            {
                result.Score += 34d;
            }

            if (actualHealerCount > 0 && dpsCount >= 2)
            {
                result.Score += enabledTemplatesIncludeHealer ? 134d : 68d;
            }

            if (actualHealerCount > 0 && carryCount >= 1)
            {
                result.Score += enabledTemplatesIncludeHealer ? 82d : 36d;
            }

            if (healerOffensiveUtilityCount > 0)
            {
                result.Score += enabledTemplatesIncludeHealer ? 148d : 84d;
            }

            if (healerRequestedMultiplierCount > 0)
            {
                result.Score += enabledTemplatesIncludeHealer ? 118d : 72d;
            }

            if (enabledTemplatesIncludeHealer && actualHealerCount == 0)
            {
                result.Score -= 320d;
            }

            if (supportOnlyEnablerCount > 0 && actualHealerCount == 0)
            {
                result.Score -= 185d;
            }

            if (actualHealerCount > 0 && dpsCount < 2 && enablerCount >= 2)
            {
                result.Score -= enabledTemplatesIncludeHealer ? 168d : 92d;
            }

            if (carryCount == 1 && enablerCount == 0 && sustainCount >= 1)
            {
                result.Score -= 60d;
            }

            if (dpsCount < 2 && carryCount == 1 && enablerCount >= 2)
            {
                result.Score -= 72d;
            }

            if (carryCount >= 2 && actualHealerCount > 0 && healerOffensiveUtilityCount == 0)
            {
                result.Score -= enabledTemplatesIncludeHealer ? 190d : 120d;
            }

            if (actualHealerCount > 0 && healerOffensiveUtilityCount > 0 && healerRequestedMultiplierCount == 0)
            {
                result.Score -= enabledTemplatesIncludeHealer ? 54d : 28d;
            }

            if (actualHealerCount > 0 && sustainCount > 0 && enablerCount == 0)
            {
                result.Score -= enabledTemplatesIncludeHealer ? 132d : 84d;
            }

            if (sustainCount >= 2)
            {
                result.Score -= 125d;
            }

            if (carryCount >= 2 && actualHealerCount == 0 && sustainCount == 0)
            {
                result.Score -= enabledTemplatesIncludeHealer ? 210d : 82d;
            }

            if (topCarryPressure >= 620d)
            {
                result.Score += 36d;
            }
            else if (carryCount > 0 && topCarryPressure < 450d)
            {
                result.Score -= 42d;
            }

            if (lowUsePenalty > 0.001)
            {
                result.Score -= lowUsePenalty;
                result.Notes.Add($"Low-actual-use offensive tax: -{lowUsePenalty:0.##}.");
            }

            if (request.EnemyWeakness != Element.None)
            {
                if (requestedElementDpsCount > 0)
                {
                    result.Score += 34d + ((requestedElementDpsCount - 1) * 52d);
                }

                if (requestedElementDpsCount >= 2)
                {
                    result.Score += actualHealerCount > 0
                        ? (enabledTemplatesIncludeHealer ? 176d : 112d)
                        : 94d;
                }

                if (dpsCount >= 2 && requestedElementDpsCount < 2)
                {
                    result.Score -= 118d;
                }

                if (dpsCount >= 2 && requestedElementDpsCount == 1)
                {
                    result.Score -= actualHealerCount > 0
                        ? (enabledTemplatesIncludeHealer ? 188d : 132d)
                        : 146d;
                }

                if (requestedElementCarryCount > 0)
                {
                    result.Score += 52d + ((requestedElementCarryCount - 1) * 44d);
                    if (requestedElementCarryCount >= 2)
                    {
                        result.Score += 34d;
                    }
                }

                if (requestedElementCarryCount >= 2 && healerRequestedMultiplierCount > 0)
                {
                    result.Score += enabledTemplatesIncludeHealer ? 108d : 68d;
                }

                if (carryCount > 0 && requestedElementCarryCount == 0)
                {
                    result.Score -= 126d;
                }
                else if (carryCount >= 2 && requestedElementCarryCount < 2)
                {
                    result.Score -= 58d;
                }
            }

            result.Notes.Add($"Offensive shell roles: {string.Join(", ", inferredRoles.OrderBy(entry => entry.Variant.CharacterName, StringComparer.OrdinalIgnoreCase).Select(entry => $"{entry.Variant.CharacterName}={entry.Inference.Role}"))}.");
            return result;
        }

        private static bool ShouldPreserveContextualSupportSeedMain(SlotEvaluation main, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            if (HasStrongOffensiveEnablerMain(main.Slot, request))
            {
                return true;
            }

            if (role is not CharacterRole.Healer and not CharacterRole.Support)
            {
                return false;
            }

            var providedKeys = main.ProvidedEffectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (HasStrongOffensiveSupportKeys(providedKeys, request))
            {
                return true;
            }

            if (!providedKeys.Contains("gear_c_ability_uses"))
            {
                return false;
            }

            var relevantMultiplierKeys = GetRelevantVariantMultiplierKeys(request);
            return relevantMultiplierKeys.Any(key => providedKeys.Contains(key));
        }

        private static bool ShouldPreserveContextualSupportSeedVariant(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var offensiveRoleInference = EnsureOffensiveRoleInference(variant, request);
            if (offensiveRoleInference.Role == OffensiveRoleProfile.OffensiveEnabler
                && offensiveRoleInference.EnablerPressure >= 130d)
            {
                return true;
            }

            if (HasStrongOffensiveSupportKeys(variant.ProvidedEffectKeys, request))
            {
                return true;
            }

            if (variant.Role is not CharacterRole.Healer and not CharacterRole.Support)
            {
                return false;
            }

            var providedKeys = variant.ProvidedEffectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!providedKeys.Contains("gear_c_ability_uses"))
            {
                return false;
            }

            var relevantMultiplierKeys = GetRelevantVariantMultiplierKeys(request);
            return relevantMultiplierKeys.Any(key => providedKeys.Contains(key));
        }

        private static bool TryParseLimitedUseCount(string? rawUseCount, out int useCount)
        {
            useCount = 0;
            return !string.IsNullOrWhiteSpace(rawUseCount)
                && int.TryParse(rawUseCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out useCount)
                && useCount > 0;
        }

        private static double GetVariantWeaponOrientationSelectionBonus(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var mainProvidedEffectKeys = DetectEffectKeys(variant.MainWeapon.ProvidedEffectLabels, variant.MainWeapon.AbilityText, request, request.BossImmunityKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyCollection<string> offHandProvidedEffectKeys = variant.OffHandWeapon == null
                ? Array.Empty<string>()
                : DetectEffectKeys(variant.OffHandWeapon.ProvidedEffectLabels, variant.OffHandWeapon.AbilityText, request, request.BossImmunityKeys)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return GetVariantWeaponOrientationBonus(
                variant.Role,
                variant.MainPassivePoints,
                variant.OffPassivePoints,
                mainProvidedEffectKeys,
                offHandProvidedEffectKeys,
                request);
        }

        private static double GetVariantWeaponOrientationBonus(
            CharacterRole role,
            IReadOnlyDictionary<string, int> mainPassivePoints,
            IReadOnlyDictionary<string, int> offPassivePoints,
            IReadOnlyCollection<string> mainProvidedEffectKeys,
            IReadOnlyCollection<string>? offHandProvidedEffectKeys,
            PlayerPowerAnalyzerV2Request request)
        {
            if (offPassivePoints.Count == 0)
            {
                return 0d;
            }

            var mainTeamPriorityScore = ScorePassivePointsByPredicate(mainPassivePoints, role, request, IsAnchorPriorityTeamWidePassive);
            var offTeamPriorityScore = ScorePassivePointsByPredicate(offPassivePoints, role, request, IsAnchorPriorityTeamWidePassive);
            var mainSelfPriorityScore = ScorePassivePointsByPredicate(mainPassivePoints, role, request, IsAnchorPrioritySelfPassive);
            var offSelfPriorityScore = ScorePassivePointsByPredicate(offPassivePoints, role, request, IsAnchorPrioritySelfPassive);
            var mainSupportUtilityScore = ScorePassivePointsByPredicate(mainPassivePoints, role, request, IsOrientationSupportUtilityPassive);
            var offSupportUtilityScore = ScorePassivePointsByPredicate(offPassivePoints, role, request, IsOrientationSupportUtilityPassive);

            if (mainTeamPriorityScore <= 0.001
                && offTeamPriorityScore <= 0.001
                && mainSelfPriorityScore <= 0.001
                && offSelfPriorityScore <= 0.001)
            {
                return 0d;
            }

            var bonus = 0d;
            var mainKeys = mainProvidedEffectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var offKeys = (offHandProvidedEffectKeys ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (role == CharacterRole.DPS)
            {
                bonus += (mainSelfPriorityScore - offSelfPriorityScore) * 0.22;
                bonus += (mainTeamPriorityScore - offTeamPriorityScore) * 0.14;
                return bonus;
            }

            // A requested-axis team-wide offensive passive (e.g. Boost ATK (All Allies)) belongs in the
            // full-value main hand: the orientation signal must be strong enough to decide main/off over
            // active/fit noise, since actives fire from either slot and only passives are full-vs-half.
            bonus += (mainTeamPriorityScore - offTeamPriorityScore) * 1.5;

            if (mainTeamPriorityScore > 0.001 || offTeamPriorityScore > 0.001)
            {
                bonus += (offSupportUtilityScore - mainSupportUtilityScore) * 0.50;
            }

            if (mainTeamPriorityScore > offTeamPriorityScore && offSupportUtilityScore > 0.001)
            {
                bonus += Math.Min(offSupportUtilityScore, mainTeamPriorityScore) * 0.10;
            }

            if (offTeamPriorityScore > mainTeamPriorityScore && mainSupportUtilityScore > 0.001)
            {
                bonus -= Math.Min(mainSupportUtilityScore, offTeamPriorityScore) * 0.08;
            }

            var mainHasRequestedAxisFoundation = request.PreferredDamageType switch
            {
                DamageType.Physical => mainKeys.Any(key => key is "patk_up" or "pdef_down" or "phys_damage_bonus" or "phys_weapon_boost" or "elemental_damage_bonus" or "elemental_weapon_boost"),
                DamageType.Magical => mainKeys.Any(key => key is "matk_up" or "mdef_down" or "mag_damage_bonus" or "mag_weapon_boost" or "elemental_damage_bonus" or "elemental_weapon_boost"),
                _ => mainKeys.Any(key => key is "patk_up" or "matk_up" or "pdef_down" or "mdef_down" or "phys_damage_bonus" or "phys_weapon_boost" or "mag_damage_bonus" or "mag_weapon_boost" or "elemental_damage_bonus" or "elemental_weapon_boost")
            };
            var offIsHealingSupportOnly = offKeys.Count > 0
                && offKeys.All(key => key.Equals("healing_support", StringComparison.OrdinalIgnoreCase));
            if (mainHasRequestedAxisFoundation
                && offIsHealingSupportOnly
                && mainTeamPriorityScore > offTeamPriorityScore
                && offSupportUtilityScore > 0.001)
            {
                bonus -= Math.Min(offSupportUtilityScore, Math.Max(mainTeamPriorityScore, 1d)) * 0.42;
            }

            return bonus * 1.55;
        }

        private static double ScorePassivePointsByPredicate(
            IReadOnlyDictionary<string, int> passivePoints,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request,
            Func<string, PlayerPowerAnalyzerV2Request, bool> predicate)
        {
            var score = 0d;
            foreach (var kvp in passivePoints)
            {
                if (kvp.Value <= 0 || !predicate(kvp.Key, request))
                {
                    continue;
                }

                score += ScorePassiveSkill(kvp.Key, kvp.Value, role, request);
            }

            return score;
        }

        private static bool IsOrientationSupportUtilityPassive(string skillName, PlayerPowerAnalyzerV2Request request)
        {
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return false;
            }

            var normalized = skillName.ToLowerInvariant();
            return IsBuffDebuffExtensionPassive(normalized)
                || normalized.Contains("heal", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("all (cure spells)", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("all (esuna)", StringComparison.OrdinalIgnoreCase);
        }

        private static double GetVariantOffHandComplementBonus(
            IReadOnlyCollection<string> baselineProvidedEffectKeys,
            IReadOnlyCollection<string> offHandProvidedEffectKeys,
            IReadOnlyCollection<DetectedActiveEffect>? baselineDetectedEffects,
            IReadOnlyCollection<DetectedActiveEffect>? offHandDetectedEffects,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request)
        {
            if (role is not CharacterRole.Healer and not CharacterRole.Support)
            {
                return 0d;
            }

            var baselineKeys = baselineProvidedEffectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var offKeys = offHandProvidedEffectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var baselineEffects = baselineDetectedEffects ?? Array.Empty<DetectedActiveEffect>();
            var offEffects = offHandDetectedEffects ?? Array.Empty<DetectedActiveEffect>();
            var relevantMultiplierKeys = GetRelevantVariantMultiplierKeys(request);
            var baselineHasFoundation = baselineKeys.Contains("patk_up")
                || baselineKeys.Contains("matk_up")
                || baselineKeys.Contains("pdef_down")
                || baselineKeys.Contains("mdef_down")
                || baselineEffects.Any(effect => IsRequestedElementalResistanceDownEffect(effect, request));
            var enhancementKeys = GetRelevantEnhancementKeys(request);
            var baselineHasEnhancement = enhancementKeys.Any(key => baselineKeys.Contains(key));
            var offAddsRelevantMultiplier = relevantMultiplierKeys.Any(key => offKeys.Contains(key) && !baselineKeys.Contains(key));
            var offAddsEnhancement = enhancementKeys.Any(key => offKeys.Contains(key) && !baselineKeys.Contains(key));
            var offAddsAmplifier = new[] { "exploit_weakness", "enfeeble", "enliven", "torpor", "stat_buff_tier_increase", "stat_debuff_tier_increase", "atb_conservation", "atb_gain" }
                .Any(key => offKeys.Contains(key) && !baselineKeys.Contains(key));
            var offAddsFoundation = request.PreferredDamageType switch
            {
                DamageType.Physical => offKeys.Any(key => key is "patk_up" or "pdef_down")
                    || offEffects.Any(effect => IsRequestedElementalResistanceDownEffect(effect, request)),
                DamageType.Magical => offKeys.Any(key => key is "matk_up" or "mdef_down")
                    || offEffects.Any(effect => IsRequestedElementalResistanceDownEffect(effect, request)),
                _ => offKeys.Any(key => key is "patk_up" or "matk_up" or "pdef_down" or "mdef_down")
                    || offEffects.Any(effect => effect.Key.Equals("elemental_resistance_down", StringComparison.OrdinalIgnoreCase))
            };
            var offAddsOperationalUtility = offKeys.Any(key => key is "haste" or "atb_conservation" or "atb_gain");
            var offHasPhysicalSupportSignal = offKeys.Any(key => key is "patk_up" or "pdef_down" or "phys_damage_bonus" or "phys_weapon_boost");
            var offHasMagicalSupportSignal = offKeys.Any(key => key is "matk_up" or "mdef_down" or "mag_damage_bonus" or "mag_weapon_boost");
            var baselineAddsOnlyOffAxisMultiplier = request.PreferredDamageType switch
            {
                DamageType.Physical => baselineKeys.Any(key => key is "mag_damage_bonus" or "mag_weapon_boost")
                    && !baselineKeys.Any(key => key is "phys_damage_bonus" or "phys_weapon_boost" or "elemental_damage_bonus" or "elemental_weapon_boost"),
                DamageType.Magical => baselineKeys.Any(key => key is "phys_damage_bonus" or "phys_weapon_boost")
                    && !baselineKeys.Any(key => key is "mag_damage_bonus" or "mag_weapon_boost" or "elemental_damage_bonus" or "elemental_weapon_boost"),
                _ => false
            };
            var offAddsOnlyOffAxisSupport = request.PreferredDamageType switch
            {
                DamageType.Physical => offHasMagicalSupportSignal && !offHasPhysicalSupportSignal && !offAddsFoundation,
                DamageType.Magical => offHasPhysicalSupportSignal && !offHasMagicalSupportSignal && !offAddsFoundation,
                _ => false
            };
            var offDuplicatesAttackBuff = (offKeys.Contains("patk_up") && baselineKeys.Contains("patk_up"))
                || (offKeys.Contains("matk_up") && baselineKeys.Contains("matk_up"));
            var offIsHealingSupportOnly = offKeys.Count > 0
                && offKeys.All(key => key.Equals("healing_support", StringComparison.OrdinalIgnoreCase));
            var baselineIsHealingSupportOnly = baselineKeys.Count > 0
                && baselineKeys.All(key => key.Equals("healing_support", StringComparison.OrdinalIgnoreCase));

            var bonus = 0d;
            if (offAddsRelevantMultiplier)
            {
                bonus += role == CharacterRole.Healer ? 128d : 116d;
                if (baselineHasFoundation && baselineHasEnhancement)
                {
                    bonus += role == CharacterRole.Healer ? 104d : 92d;
                }
            }

            if (!offAddsRelevantMultiplier && offAddsEnhancement)
            {
                bonus += role == CharacterRole.Healer ? 72d : 64d;
            }

            if (offAddsAmplifier)
            {
                bonus += role == CharacterRole.Healer ? 34d : 30d;
            }

            if (offDuplicatesAttackBuff && !offAddsRelevantMultiplier && !offAddsEnhancement && !offAddsAmplifier)
            {
                bonus -= role == CharacterRole.Healer ? 136d : 118d;
                if (baselineHasFoundation && baselineHasEnhancement)
                {
                    bonus -= role == CharacterRole.Healer ? 76d : 64d;
                }
            }

            if (baselineHasFoundation
                && !offAddsFoundation
                && !offAddsRelevantMultiplier
                && !offAddsEnhancement
                && !offAddsAmplifier
                && !offAddsOperationalUtility)
            {
                if (offKeys.Count == 0)
                {
                    bonus -= role == CharacterRole.Healer ? 152d : 136d;
                }
                else if (offIsHealingSupportOnly)
                {
                    bonus -= role == CharacterRole.Healer ? 188d : 166d;
                }
                else
                {
                    bonus -= role == CharacterRole.Healer ? 126d : 112d;
                }
            }

            if (baselineHasFoundation
                && offAddsOnlyOffAxisSupport
                && !offAddsRelevantMultiplier
                && !offAddsEnhancement
                && !offAddsAmplifier)
            {
                bonus -= role == CharacterRole.Healer ? 178d : 156d;
                if (baselineHasEnhancement)
                {
                    bonus -= role == CharacterRole.Healer ? 84d : 72d;
                }
            }

            if (baselineAddsOnlyOffAxisMultiplier
                && offAddsFoundation
                && !offAddsRelevantMultiplier
                && !offAddsEnhancement)
            {
                bonus -= role == CharacterRole.Healer ? 244d : 216d;
                if (offAddsOperationalUtility)
                {
                    bonus -= role == CharacterRole.Healer ? 48d : 42d;
                }
            }

            if (baselineIsHealingSupportOnly
                && offAddsFoundation
                && !offAddsRelevantMultiplier
                && !offAddsEnhancement)
            {
                bonus -= role == CharacterRole.Healer ? 168d : 148d;
                if (offAddsOperationalUtility)
                {
                    bonus -= role == CharacterRole.Healer ? 24d : 20d;
                }
            }

            return bonus;
        }

        private static string[] GetRelevantVariantMultiplierKeys(PlayerPowerAnalyzerV2Request request)
        {
            return request.PreferredDamageType switch
            {
                DamageType.Physical => new[] { "phys_damage_bonus", "phys_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" },
                DamageType.Magical => new[] { "mag_damage_bonus", "mag_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" },
                _ => new[] { "phys_damage_bonus", "phys_weapon_boost", "mag_damage_bonus", "mag_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" }
            };
        }

        private static string[] GetRelevantDamageReceivedUpKeys(PlayerPowerAnalyzerV2Request request)
        {
            return request.PreferredDamageType switch
            {
                DamageType.Physical => new[] { "phys_damage_received_up", "elemental_damage_received_up" },
                DamageType.Magical => new[] { "mag_damage_received_up", "elemental_damage_received_up" },
                _ => new[] { "phys_damage_received_up", "mag_damage_received_up", "elemental_damage_received_up" }
            };
        }

        private static string[] GetRelevantEnhancementKeys(PlayerPowerAnalyzerV2Request request)
        {
            return new[] { "elemental_damage_up" }
                .Concat(GetRelevantDamageReceivedUpKeys(request))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static EffectiveSubWeaponRoleInference InferEffectiveSubWeaponRole(
            CharacterRole baseRole,
            PlayerPowerAnalyzerV2ItemSlot mainWeapon,
            PlayerPowerAnalyzerV2ItemSlot? offHandWeapon,
            PlayerPowerAnalyzerV2ItemSlot? ultimateWeapon,
            PlayerPowerAnalyzerV2ItemSlot? mainOutfit,
            IReadOnlyCollection<string> providedEffectKeys,
            IReadOnlyDictionary<string, int> passivePoints,
            PlayerPowerAnalyzerV2Request request)
        {
            var totalPatk = mainWeapon.Patk + (offHandWeapon?.Patk ?? 0) + (ultimateWeapon?.Patk ?? 0) + (mainOutfit?.Patk ?? 0);
            var totalMatk = mainWeapon.Matk + (offHandWeapon?.Matk ?? 0) + (ultimateWeapon?.Matk ?? 0) + (mainOutfit?.Matk ?? 0);
            var totalHeal = mainWeapon.Heal + (offHandWeapon?.Heal ?? 0) + (ultimateWeapon?.Heal ?? 0) + (mainOutfit?.Heal ?? 0);
            var primaryOffenseStat = request.PreferredDamageType switch
            {
                DamageType.Physical => totalPatk,
                DamageType.Magical => totalMatk,
                _ => Math.Max(totalPatk, totalMatk)
            };
            var supportSignalCount = CountMatchingKeys(
                providedEffectKeys,
                GetRelevantDamageReceivedUpKeys(request)
                    .Concat(new[]
                    {
                        "patk_up",
                        "matk_up",
                        "pdef_down",
                        "mdef_down",
                        "elemental_resistance_down",
                        "elemental_damage_up",
                        "elemental_damage_bonus",
                        "elemental_weapon_boost",
                        "exploit_weakness",
                        "enfeeble",
                        "enliven",
                        "torpor",
                        "stat_buff_tier_increase",
                        "stat_debuff_tier_increase",
                        "atb_conservation",
                        "atb_gain"
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            var amplifierSignalCount = CountMatchingKeys(
                providedEffectKeys,
                "exploit_weakness",
                "enfeeble",
                "enliven",
                "torpor",
                "stat_buff_tier_increase",
                "stat_debuff_tier_increase",
                "atb_conservation",
                "atb_gain");
            var defenseSignalCount = CountMatchingKeys(providedEffectKeys, "pdef_up", "mdef_up");
            var relevantMultiplierCount = CountMatchingKeys(providedEffectKeys, GetRelevantVariantMultiplierKeys(request));
            var requestedDamageAlignmentCount = new[] { mainWeapon, offHandWeapon, ultimateWeapon }
                .Where(slot => slot != null)
                .Count(slot => MatchesRequestedDamageType(slot!.AbilityType, request.PreferredDamageType));
            var requestedElementAlignmentCount = request.EnemyWeakness == Element.None
                ? 0
                : new[] { mainWeapon, offHandWeapon, ultimateWeapon }
                    .Where(slot => slot != null)
                    .Count(slot => !string.IsNullOrWhiteSpace(slot!.Element)
                        && !slot.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                        && MatchesRequestedElement(slot.Element, request.EnemyWeakness));
            var anchorSignalCount = CountAnchorOffensiveSignals(providedEffectKeys, request);
            var healerPassivePoints = SumPassivePoints(passivePoints, normalized => normalized.Contains("heal", StringComparison.OrdinalIgnoreCase));
            var extensionPoints = SumPassivePoints(passivePoints, normalized => IsBuffDebuffExtensionPassive(normalized));
            var hpPassivePoints = SumPassivePoints(passivePoints, normalized => normalized.Contains("boost hp", StringComparison.OrdinalIgnoreCase));
            var localDefensePassivePoints = SumPassivePoints(
                passivePoints,
                normalized => !normalized.Contains("all allies", StringComparison.OrdinalIgnoreCase)
                    && (normalized.Contains("pdef", StringComparison.OrdinalIgnoreCase)
                        || normalized.Contains("mdef", StringComparison.OrdinalIgnoreCase)));
            var offensivePassiveScore = ScorePassivePointsByPredicate(passivePoints, CharacterRole.DPS, request, IsAnchorPrioritySelfPassive)
                + ScorePassivePointsByPredicate(passivePoints, CharacterRole.DPS, request, IsAnchorPriorityTeamWidePassive);

            var scores = new Dictionary<CharacterRole, double>
            {
                [CharacterRole.DPS] = GetBaseRoleInferenceBias(baseRole, CharacterRole.DPS),
                [CharacterRole.Support] = GetBaseRoleInferenceBias(baseRole, CharacterRole.Support),
                [CharacterRole.Healer] = GetBaseRoleInferenceBias(baseRole, CharacterRole.Healer),
                [CharacterRole.Tank] = GetBaseRoleInferenceBias(baseRole, CharacterRole.Tank)
            };

            scores[CharacterRole.DPS] += requestedDamageAlignmentCount * 0.8;
            scores[CharacterRole.DPS] += requestedElementAlignmentCount * 0.72;
            scores[CharacterRole.DPS] += anchorSignalCount * 0.32;
            scores[CharacterRole.DPS] += offensivePassiveScore * 0.008;
            if (primaryOffenseStat > 0 && totalHeal < primaryOffenseStat * 0.82)
            {
                scores[CharacterRole.DPS] += 0.62;
            }

            scores[CharacterRole.Support] += supportSignalCount * 0.58;
            scores[CharacterRole.Support] += amplifierSignalCount * 0.24;
            scores[CharacterRole.Support] += relevantMultiplierCount * 0.36;
            if (extensionPoints > 0)
            {
                scores[CharacterRole.Support] += 0.72;
            }

            if (supportSignalCount >= 3 && relevantMultiplierCount > 0)
            {
                scores[CharacterRole.Support] += 0.44;
            }

            scores[CharacterRole.Healer] += healerPassivePoints > 0 ? 1.08 : 0d;
            scores[CharacterRole.Healer] += extensionPoints > 0 ? 0.66 : 0d;
            scores[CharacterRole.Healer] += defenseSignalCount * 0.16;
            if (primaryOffenseStat > 0)
            {
                var healRatio = (double)totalHeal / primaryOffenseStat;
                if (healRatio >= 1.0)
                {
                    scores[CharacterRole.Healer] += 1.1;
                }
                else if (healRatio >= 0.8)
                {
                    scores[CharacterRole.Healer] += 0.62;
                }
            }
            else if (totalHeal > 0)
            {
                scores[CharacterRole.Healer] += 0.7;
            }

            scores[CharacterRole.Tank] += defenseSignalCount == 2 ? 1.18 : defenseSignalCount * 0.3;
            scores[CharacterRole.Tank] += hpPassivePoints > 0 ? 0.72 : 0d;
            scores[CharacterRole.Tank] += localDefensePassivePoints > 0 ? 0.84 : 0d;

            if (supportSignalCount >= 3 && healerPassivePoints <= 0 && totalHeal < primaryOffenseStat * 0.85)
            {
                scores[CharacterRole.Healer] -= 0.36;
            }

            var chosenRole = scores
                .OrderByDescending(entry => entry.Value)
                .ThenByDescending(entry => GetRoleInferencePreference(entry.Key, baseRole))
                .Select(entry => entry.Key)
                .First();
            if (chosenRole != baseRole)
            {
                var requiredMargin = chosenRole == CharacterRole.DPS && baseRole != CharacterRole.DPS ? 0.9 : 0.3;
                if (scores[chosenRole] - scores[baseRole] < requiredMargin)
                {
                    chosenRole = baseRole;
                }
            }

            var reasonPrefix = chosenRole == baseRole
                ? $"preserved base role {baseRole}"
                : $"shifted from {baseRole} to {chosenRole}";
            var offenseRatio = primaryOffenseStat <= 0 ? 0d : Math.Round((double)totalHeal / primaryOffenseStat, 2);
            var reason = $"{reasonPrefix}; scores DPS={scores[CharacterRole.DPS]:0.##}, Support={scores[CharacterRole.Support]:0.##}, Healer={scores[CharacterRole.Healer]:0.##}, Tank={scores[CharacterRole.Tank]:0.##}; supportKeys={supportSignalCount}, amplifiers={amplifierSignalCount}, defenseKeys={defenseSignalCount}, healToOffense={offenseRatio:0.##}.";
            return new EffectiveSubWeaponRoleInference
            {
                Role = chosenRole,
                Reason = reason
            };
        }

        private static int CountMatchingKeys(IReadOnlyCollection<string> keys, params string[] targetKeys)
        {
            if (keys.Count == 0 || targetKeys.Length == 0)
            {
                return 0;
            }

            return targetKeys.Count(target => keys.Contains(target, StringComparer.OrdinalIgnoreCase));
        }

        private static double SumPassivePoints(IReadOnlyDictionary<string, int> passivePoints, Func<string, bool> predicate)
        {
            return passivePoints
                .Where(kvp => kvp.Value > 0 && predicate(kvp.Key.ToLowerInvariant()))
                .Sum(kvp => kvp.Value);
        }

        private static double GetBaseRoleInferenceBias(CharacterRole baseRole, CharacterRole candidateRole)
        {
            if (baseRole == candidateRole)
            {
                return 1.2;
            }

            return candidateRole switch
            {
                CharacterRole.Support => 0.42,
                CharacterRole.Healer => 0.38,
                CharacterRole.Tank => 0.24,
                CharacterRole.DPS => 0.22,
                _ => 0.2
            };
        }

        private static int GetRoleInferencePreference(CharacterRole candidateRole, CharacterRole baseRole)
        {
            if (candidateRole == baseRole)
            {
                return 5;
            }

            return candidateRole switch
            {
                CharacterRole.Support => 4,
                CharacterRole.Healer => 3,
                CharacterRole.DPS => 2,
                CharacterRole.Tank => 1,
                _ => 0
            };
        }

        private static double GetFinalTeamRedundantOffHandPenalty(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (variant.OffHandWeapon == null)
            {
                return 0d;
            }

            var detectedEffects = GetDetectedEffectsForVariant(variant, request);
            var baselineProvidedKeys = detectedEffects
                .Where(effect => effect.SourceType.Equals("Main Weapon", StringComparison.OrdinalIgnoreCase)
                    || effect.SourceType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase)
                    || effect.SourceType.Equals("Main Outfit", StringComparison.OrdinalIgnoreCase))
                .Select(effect => effect.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var offHandProvidedKeys = detectedEffects
                .Where(effect => effect.SourceType.Equals("Off-hand", StringComparison.OrdinalIgnoreCase))
                .Select(effect => effect.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var baselineDetectedEffects = detectedEffects
                .Where(effect => effect.SourceType.Equals("Main Weapon", StringComparison.OrdinalIgnoreCase)
                    || effect.SourceType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase)
                    || effect.SourceType.Equals("Main Outfit", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var offHandDetectedEffects = detectedEffects
                .Where(effect => effect.SourceType.Equals("Off-hand", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var complementBonus = GetVariantOffHandComplementBonus(baselineProvidedKeys, offHandProvidedKeys, baselineDetectedEffects, offHandDetectedEffects, variant.Role, request);
            return complementBonus < 0d
                ? complementBonus * 1.65
                : 0d;
        }

        private static double GetFinalTeamRedundantOffHandPenalty(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            PlayerPowerAnalyzerV2Request request)
        {
            return baseVariants.Sum(variant => GetFinalTeamRedundantOffHandPenalty(variant, request));
        }

        private static double GetFinalTeamVariantAlignmentPenalty(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            PlayerPowerAnalyzerV2Request request)
        {
            return baseVariants.Sum(variant => GetVariantOffAxisPackagePenalty(
                variant,
                variant.ProvidedEffectKeys,
                GetDetectedEffectsForVariant(variant, request),
                request) * 1.5);
        }

        private static double GetAnchorCandidateSelectionBonus(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (variant.Role != CharacterRole.DPS)
            {
                return 0d;
            }

            var candidateScore = ScoreAnchorCandidate(variant, request);
            return Math.Max(0d, (candidateScore - 260d) * 0.08);
        }

        private static double ScoreAnchorCandidate(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            var offensiveRoleInference = EnsureOffensiveRoleInference(variant, request);
            var score = variant.Role switch
            {
                CharacterRole.DPS => 240d,
                CharacterRole.Support => 120d,
                CharacterRole.Healer => 105d,
                CharacterRole.Tank => 90d,
                _ => 100d
            };

            score += offensiveRoleInference.Role switch
            {
                OffensiveRoleProfile.Carry => 54d,
                OffensiveRoleProfile.OffensiveEnabler => -38d,
                OffensiveRoleProfile.SustainSupport => -68d,
                OffensiveRoleProfile.DefensiveSupport => -86d,
                _ => 0d
            };

            if (request.PreferredDamageType == DamageType.Any || MatchesRequestedDamageType(variant.MainWeapon.AbilityType, request.PreferredDamageType))
            {
                score += variant.Role == CharacterRole.DPS ? 110d : 35d;
            }
            else if (!string.IsNullOrWhiteSpace(variant.MainWeapon.AbilityType))
            {
                score -= variant.Role == CharacterRole.DPS ? 90d : 20d;
            }

            if (request.EnemyWeakness == Element.None)
            {
                score += variant.Role == CharacterRole.DPS ? 105d : 25d;
            }
            else
            {
                var hasRequestedElementAxis = HasRequestedElementMainOrOffHand(variant, request);
                var hasExplicitOffAxisElement = new[] { variant.MainWeapon, variant.OffHandWeapon }
                    .Where(slot => slot != null)
                    .Any(slot => !string.IsNullOrWhiteSpace(slot!.Element)
                        && !slot.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                        && !MatchesRequestedElement(slot.Element, request.EnemyWeakness));
                if (hasRequestedElementAxis)
                {
                    score += variant.Role == CharacterRole.DPS ? 132d : 36d;
                }
                else if (hasExplicitOffAxisElement)
                {
                    score -= variant.Role == CharacterRole.DPS ? 95d : 20d;
                }
                else
                {
                    score += variant.Role == CharacterRole.DPS ? 12d : 6d;
                }
            }

            var relevantMainStat = request.PreferredDamageType switch
            {
                DamageType.Physical => variant.MainWeapon.Patk,
                DamageType.Magical => variant.MainWeapon.Matk,
                _ => Math.Max(variant.MainWeapon.Patk, variant.MainWeapon.Matk)
            };
            score += relevantMainStat * (variant.Role == CharacterRole.DPS ? 0.18 : 0.06);
            score += variant.MainWeapon.DamagePercent * (variant.Role == CharacterRole.DPS ? 0.12 : 0.04);
            score += offensiveRoleInference.DirectPressure * (variant.Role == CharacterRole.DPS ? 0.11 : 0.04);
            score += CountAnchorOffensiveSignals(variant.ProvidedEffectKeys, request) * (variant.Role == CharacterRole.DPS ? 22d : 8d);
            return score;
        }

        private static int CountAnchorOffensiveSignals(IEnumerable<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request)
        {
            var relevantKeys = GetAnchorPriorityKeys(request);
            return providedEffectKeys.Count(key => relevantKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
        }

        private static AnchorScoringContext? BuildAnchorScoringContext(IReadOnlyList<CharacterBuildCandidate> baseVariants, PlayerPowerAnalyzerV2Request request)
        {
            var anchor = baseVariants
                .OrderByDescending(variant => ScoreAnchorCandidate(variant, request))
                .ThenBy(variant => variant.CharacterName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (anchor == null)
            {
                return null;
            }

            var activeSourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                anchor.MainWeapon.Name
            };
            if (anchor.OffHandWeapon != null)
            {
                activeSourceNames.Add(anchor.OffHandWeapon.Name);
            }

            if (anchor.UltimateWeapon != null)
            {
                activeSourceNames.Add(anchor.UltimateWeapon.Name);
            }

            if (anchor.MainOutfit != null)
            {
                activeSourceNames.Add(anchor.MainOutfit.Name);
            }

            var anchorDetectedEffects = GetDetectedEffectsForVariant(anchor, request);

            return new AnchorScoringContext
            {
                CharacterName = anchor.CharacterName,
                MainWeaponName = anchor.MainWeapon.Name,
                CandidateScore = ScoreAnchorCandidate(anchor, request),
                ActiveSourceNames = activeSourceNames,
                MissingPriorityKeys = GetAnchorPriorityKeys(request)
                    .Where(key => key.Equals("elemental_resistance_down", StringComparison.OrdinalIgnoreCase)
                        ? request.EnemyWeakness == Element.None
                            ? !anchor.ProvidedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
                            : !anchorDetectedEffects.Any(effect => IsRequestedElementalResistanceDownEffect(effect, request))
                        : !anchor.ProvidedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private static List<string> GetAnchorPriorityKeys(PlayerPowerAnalyzerV2Request request)
        {
            var keys = new List<string>
            {
                "elemental_resistance_down",
                "elemental_damage_up",
                "exploit_weakness",
                "enfeeble",
                "enliven",
                "torpor",
                "stat_buff_tier_increase",
                "stat_debuff_tier_increase"
            };
            keys.AddRange(GetRelevantDamageReceivedUpKeys(request));

            if (request.PreferredDamageType == DamageType.Magical)
            {
                keys.InsertRange(0, new[] { "matk_up", "mdef_down", "mag_damage_bonus", "mag_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" });
            }
            else if (request.PreferredDamageType == DamageType.Physical)
            {
                keys.InsertRange(0, new[] { "patk_up", "pdef_down", "phys_damage_bonus", "phys_weapon_boost", "elemental_damage_bonus", "elemental_weapon_boost" });
            }
            else
            {
                keys.InsertRange(0, new[] { "patk_up", "matk_up", "pdef_down", "mdef_down", "elemental_damage_bonus", "elemental_weapon_boost" });
            }

            return keys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AnchorSupportScoreResult ScoreAnchorSupportSynergy(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            IReadOnlyList<DetectedActiveEffect> explicitDetectedEffects,
            PlayerPowerAnalyzerV2Request request)
        {
            var result = new AnchorSupportScoreResult();
            var anchorContext = BuildAnchorScoringContext(baseVariants, request);
            if (anchorContext == null)
            {
                return result;
            }

            result.AnchorContext = anchorContext;
            if (explicitDetectedEffects.Count == 0)
            {
                return result;
            }

            var allyDetectedEffects = explicitDetectedEffects
                .Where(effect => !anchorContext.ActiveSourceNames.Contains(effect.SourceName))
                .Where(IsAnchorSupportEligibleEffect)
                .ToList();
            var allyExplicitKeys = explicitDetectedEffects
                .Where(effect => !anchorContext.ActiveSourceNames.Contains(effect.SourceName))
                .Where(IsAnchorSupportEligibleEffect)
                .Select(effect => effect.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var teamExplicitKeys = explicitDetectedEffects
                .Select(effect => effect.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allyHasRequestedElementalResDown = request.EnemyWeakness == Element.None
                ? allyExplicitKeys.Contains("elemental_resistance_down")
                : allyDetectedEffects.Any(effect => IsRequestedElementalResistanceDownEffect(effect, request));
            var teamHasRequestedElementalResDown = request.EnemyWeakness == Element.None
                ? teamExplicitKeys.Contains("elemental_resistance_down")
                : explicitDetectedEffects.Any(effect => IsRequestedElementalResistanceDownEffect(effect, request));

            foreach (var key in anchorContext.MissingPriorityKeys)
            {
                if (key.Equals("elemental_resistance_down", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allyHasRequestedElementalResDown)
                    {
                        continue;
                    }
                }
                else if (!allyExplicitKeys.Contains(key))
                {
                    continue;
                }

                result.Score += GetAnchorPrioritySupportWeight(key, request);
                result.CoveredPriorityLabels.Add(ToLabel(key));
            }

            if (allyExplicitKeys.Contains("stat_buff_tier_increase")
                && HasAnyKey(teamExplicitKeys, "patk_up", "matk_up", "elemental_damage_up", "elemental_damage_bonus", "elemental_weapon_boost", "phys_damage_bonus", "phys_weapon_boost", "mag_damage_bonus", "mag_weapon_boost"))
            {
                result.Score += request.PreferredDamageType == DamageType.Magical ? 44d : 40d;
                result.AmplifierLabels.Add(ToLabel("stat_buff_tier_increase"));
            }

            if (allyExplicitKeys.Contains("stat_debuff_tier_increase")
                && (HasAnyKey(teamExplicitKeys, GetRelevantDamageReceivedUpKeys(request).Concat(new[] { "pdef_down", "mdef_down" }).ToArray())
                    || teamHasRequestedElementalResDown))
            {
                result.Score += request.PreferredDamageType == DamageType.Magical ? 46d : 42d;
                result.AmplifierLabels.Add(ToLabel("stat_debuff_tier_increase"));
            }

            if (allyExplicitKeys.Contains("enliven")
                && HasAnyKey(teamExplicitKeys, "patk_up", "matk_up", "elemental_damage_up", "elemental_damage_bonus", "elemental_weapon_boost", "phys_damage_bonus", "phys_weapon_boost", "mag_damage_bonus", "mag_weapon_boost"))
            {
                result.Score += 32d;
                result.AmplifierLabels.Add(ToLabel("enliven"));
            }

            if ((allyExplicitKeys.Contains("atb_conservation") || allyExplicitKeys.Contains("atb_gain"))
                && HasAnyKey(teamExplicitKeys, "patk_up", "matk_up", "elemental_damage_up", "elemental_damage_bonus", "elemental_weapon_boost", "phys_damage_bonus", "phys_weapon_boost", "mag_damage_bonus", "mag_weapon_boost", "exploit_weakness"))
            {
                result.Score += allyExplicitKeys.Contains("atb_gain") ? 34d : 28d;
                result.AmplifierLabels.Add(ToLabel(allyExplicitKeys.Contains("atb_gain") ? "atb_gain" : "atb_conservation"));
            }

            if (allyExplicitKeys.Contains("enfeeble")
                && (HasAnyKey(teamExplicitKeys, GetRelevantDamageReceivedUpKeys(request).Concat(new[] { "pdef_down", "mdef_down" }).ToArray())
                    || teamHasRequestedElementalResDown))
            {
                result.Score += 34d;
                result.AmplifierLabels.Add(ToLabel("enfeeble"));
            }

            if (allyExplicitKeys.Contains("torpor") && anchorContext.CandidateScore > 0)
            {
                result.Score += 18d;
                result.AmplifierLabels.Add(ToLabel("torpor"));
            }

            return result;
        }

        private static bool IsAnchorSupportEligibleEffect(DetectedActiveEffect effect)
        {
            return effect.TargetScope switch
            {
                ActiveEffectTargetScope.AllAllies => true,
                ActiveEffectTargetScope.OtherAllies => true,
                ActiveEffectTargetScope.SingleAlly => true,
                ActiveEffectTargetScope.SingleEnemy => true,
                ActiveEffectTargetScope.AllEnemies => true,
                _ => false
            };
        }

        private static double GetAnchorPrioritySupportWeight(string key, PlayerPowerAnalyzerV2Request request)
        {
            return key switch
            {
                "elemental_resistance_down" => 78d,
                "elemental_damage_up" => 58d,
                "elemental_damage_received_up" => 50d,
                "elemental_damage_bonus" or "elemental_weapon_boost" => 48d,
                "patk_up" or "matk_up" => 64d,
                "pdef_down" or "mdef_down" => 66d,
                "phys_damage_bonus" or "phys_weapon_boost" => request.PreferredDamageType == DamageType.Physical ? 46d : 18d,
                "mag_damage_bonus" or "mag_weapon_boost" => request.PreferredDamageType == DamageType.Magical ? 46d : 18d,
                "phys_damage_received_up" => request.PreferredDamageType == DamageType.Physical ? 54d : 20d,
                "mag_damage_received_up" => request.PreferredDamageType == DamageType.Magical ? 54d : 20d,
                "exploit_weakness" => 38d,
                "atb_conservation" => 26d,
                "atb_gain" => 32d,
                "enfeeble" => 34d,
                "enliven" => 30d,
                "torpor" => 22d,
                "stat_buff_tier_increase" => 28d,
                "stat_debuff_tier_increase" => 30d,
                _ => 20d
            };
        }

        private static bool HasAnyKey(HashSet<string> providedEffectKeys, params string[] keys)
        {
            return keys.Any(key => providedEffectKeys.Contains(key));
        }

        private static void AppendAnchorScoringDebugNotes(List<string> debugNotes, AnchorSupportScoreResult anchorSupportResult)
        {
            if (anchorSupportResult.AnchorContext == null)
            {
                return;
            }

            debugNotes.Add($"Chosen anchor: {anchorSupportResult.AnchorContext.CharacterName} ({anchorSupportResult.AnchorContext.MainWeaponName}).");

            if (anchorSupportResult.AnchorContext.MissingPriorityKeys.Count > 0)
            {
                debugNotes.Add($"Anchor priority gaps before ally support: {string.Join(", ", anchorSupportResult.AnchorContext.MissingPriorityKeys.Take(4).Select(ToLabel))}.");
            }

            if (anchorSupportResult.CoveredPriorityLabels.Count > 0)
            {
                debugNotes.Add($"Anchor priorities covered by allies: {string.Join(", ", anchorSupportResult.CoveredPriorityLabels.Distinct(StringComparer.OrdinalIgnoreCase).Take(5))}.");
            }

            if (anchorSupportResult.AmplifierLabels.Count > 0)
            {
                debugNotes.Add($"Anchor amplifier synergies: {string.Join(", ", anchorSupportResult.AmplifierLabels.Distinct(StringComparer.OrdinalIgnoreCase))}.");
            }
        }

        // Records a single equipped slot's active-buff contribution into the per-family ledger used for
        // duplicate-buff deduplication. Effects are grouped by family (e.g. PATK Up and the assumed
        // Bravery buff both land under attack_buff); the family tracks the best single-cast potency tier
        // reached and the desired ramp target so the marginal value of a second source can be judged.
        private static void AccumulateActiveFamilyContribution(
            Dictionary<string, SlotActiveFamilyContribution> activeUtilityByFamily,
            DetectedActiveEffect effect,
            double effectScore,
            CharacterRole role)
        {
            if (effectScore <= 0)
            {
                return;
            }

            var familyKey = string.IsNullOrWhiteSpace(effect.FamilyKey) ? effect.Key : effect.FamilyKey;
            if (!activeUtilityByFamily.TryGetValue(familyKey, out var contribution))
            {
                contribution = new SlotActiveFamilyContribution();
                activeUtilityByFamily[familyKey] = contribution;
            }

            contribution.Score += effectScore;
            var tier = effect.PotencyTierRank ?? 0;
            if (tier > contribution.PotencyTier)
            {
                contribution.PotencyTier = tier;
            }

            if (IsThresholdSensitiveSetupEffect(effect.Key))
            {
                var desired = GetDesiredSetupThresholdTierRank(effect.Key, role);
                if (desired > contribution.DesiredTier)
                {
                    contribution.DesiredTier = desired;
                }
            }
        }

        // Sums the active-buff value of a character's battle-active slots (main / off-hand / ultimate /
        // main outfit) with duplicate buffs collapsed: for each effect family the strongest source is
        // counted in full and every additional same-family source is counted only at its marginal value.
        // This stops the optimizer from stacking two weapons that grant the same buff and pushes it
        // toward buff diversity instead.
        private static double ComputeDeduplicatedActiveUtility(IReadOnlyList<SlotEvaluation> activeSlots)
        {
            return ComputeDeduplicatedActiveUtility(activeSlots, out _);
        }

        // Also emits the character's net per-family active contribution (primary source in full plus the
        // marginal value of same-family secondaries), keyed by family. The net breakdown is what team
        // scoring uses to dedup the same buff across party members.
        private static double ComputeDeduplicatedActiveUtility(
            IReadOnlyList<SlotEvaluation> activeSlots,
            out Dictionary<string, SlotActiveFamilyContribution> netByFamily)
        {
            var contributionsByFamily = new Dictionary<string, List<SlotActiveFamilyContribution>>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in activeSlots)
            {
                foreach (var kvp in slot.ActiveUtilityByFamily)
                {
                    if (kvp.Value.Score <= 0)
                    {
                        continue;
                    }

                    if (!contributionsByFamily.TryGetValue(kvp.Key, out var list))
                    {
                        list = new List<SlotActiveFamilyContribution>();
                        contributionsByFamily[kvp.Key] = list;
                    }

                    list.Add(kvp.Value);
                }
            }

            netByFamily = new Dictionary<string, SlotActiveFamilyContribution>(StringComparer.OrdinalIgnoreCase);
            var total = 0d;
            foreach (var (family, contributions) in contributionsByFamily)
            {
                var ordered = contributions.OrderByDescending(contribution => contribution.Score).ToList();
                var primary = ordered[0];
                var familyTotal = primary.Score;
                var marginalFraction = GetDuplicateBuffMarginalFraction(primary);
                for (var index = 1; index < ordered.Count; index++)
                {
                    familyTotal += ordered[index].Score * marginalFraction;
                }

                total += familyTotal;
                netByFamily[family] = new SlotActiveFamilyContribution
                {
                    Score = familyTotal,
                    PotencyTier = primary.PotencyTier,
                    DesiredTier = primary.DesiredTier
                };
            }

            return total;
        }

        // The team-wide analogue of intra-character duplicate-buff dedup: when two or more party members
        // provide the same buff family, the strongest provider is kept in full and the rest are worth
        // only their marginal (refresh/ramp) value, because same-name buffs share a cap in battle. Returns
        // the amount to subtract from the team score, i.e. the value that double-counting would add.
        private static double GetTeamWideDuplicateBuffPenalty(IReadOnlyList<CharacterBuildCandidate> baseVariants)
        {
            var contributionsByFamily = new Dictionary<string, List<SlotActiveFamilyContribution>>(StringComparer.OrdinalIgnoreCase);
            foreach (var variant in baseVariants)
            {
                foreach (var kvp in variant.ActiveFamilyContributions)
                {
                    if (kvp.Value.Score <= 0)
                    {
                        continue;
                    }

                    if (!contributionsByFamily.TryGetValue(kvp.Key, out var list))
                    {
                        list = new List<SlotActiveFamilyContribution>();
                        contributionsByFamily[kvp.Key] = list;
                    }

                    list.Add(kvp.Value);
                }
            }

            var penalty = 0d;
            foreach (var contributions in contributionsByFamily.Values)
            {
                if (contributions.Count < 2)
                {
                    continue;
                }

                var ordered = contributions.OrderByDescending(contribution => contribution.Score).ToList();
                var marginalFraction = GetDuplicateBuffMarginalFraction(ordered[0]);
                for (var index = 1; index < ordered.Count; index++)
                {
                    penalty += ordered[index].Score * (1.0 - marginalFraction);
                }
            }

            return penalty;
        }

        // The fraction of full value a redundant same-family buff source is worth. A second source of a
        // buff the primary already drives to its useful tier in one cast only helps refresh/maintain it
        // (~15%). When the primary falls short of the desired tier, a second source meaningfully speeds
        // the ramp, so the duplicate is worth proportionally more (capped well below full value).
        private static double GetDuplicateBuffMarginalFraction(SlotActiveFamilyContribution primary)
        {
            const double refreshBaseFraction = 0.15;
            if (primary.DesiredTier <= 0)
            {
                return refreshBaseFraction;
            }

            var shortfall = Math.Max(0, primary.DesiredTier - primary.PotencyTier);
            return Math.Clamp(refreshBaseFraction + (shortfall * 0.18), refreshBaseFraction, 0.6);
        }

        private static CharacterBuildCandidate ComposeCharacterVariantCandidate(
            string character,
            CharacterRole role,
            SlotEvaluation main,
            SlotEvaluation? off,
            SlotEvaluation? ultimate,
            SlotEvaluation? mainCostume,
            List<PlayerPowerAnalyzerV2ItemSlot> subOutfits,
            Dictionary<string, int> subOutfitPassivePoints,
            double subOutfitScore,
            HashSet<string> usedNames,
            PlayerPowerAnalyzerV2Request request)
        {
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

            // Active buffs (PATK Up, Haste, etc.) cast by the main, off-hand, ultimate, and main outfit
            // share a cap in battle: equipping two sources of the same buff family does not double its
            // value. So the additive (stat/damage/timing) portion of each slot is summed as-is, while
            // the active-buff portion is deduplicated by family — the strongest source counts in full
            // and each additional same-family source only counts for its marginal ramp/refresh value.
            var activeSlots = new List<SlotEvaluation> { main };
            if (off != null) { activeSlots.Add(off); }
            if (ultimate != null) { activeSlots.Add(ultimate); }
            if (mainCostume != null) { activeSlots.Add(mainCostume); }

            var additivePortion = subOutfitScore;
            foreach (var slot in activeSlots)
            {
                var slotActiveTotal = slot.ActiveUtilityByFamily.Values.Sum(contribution => contribution.Score);
                additivePortion += slot.NonPassiveScore - slotActiveTotal;
            }

            var dedupedActivePortion = ComputeDeduplicatedActiveUtility(activeSlots, out var activeFamilyContributions);
            var nonPassiveScore = additivePortion + dedupedActivePortion;
            var passiveScore = ScorePassivePoints(passivePoints, role, request);
            var characterScore = nonPassiveScore + passiveScore;
            if (off != null)
            {
                characterScore += GetVariantWeaponOrientationBonus(
                    role,
                    main.PassivePoints,
                    off.PassivePoints,
                    main.ProvidedEffectKeys,
                    off.ProvidedEffectKeys,
                    request);
            }

            var providedKeys = new HashSet<string>(main.ProvidedEffectKeys, StringComparer.OrdinalIgnoreCase);
            var baselineProvidedKeys = new HashSet<string>(providedKeys, StringComparer.OrdinalIgnoreCase);

            if (ultimate != null)
            {
                foreach (var key in ultimate.ProvidedEffectKeys)
                {
                    baselineProvidedKeys.Add(key);
                    providedKeys.Add(key);
                }
            }

            if (mainCostume != null)
            {
                foreach (var key in mainCostume.ProvidedEffectKeys)
                {
                    baselineProvidedKeys.Add(key);
                    providedKeys.Add(key);
                }
            }

            if (off != null)
            {
                foreach (var key in off.ProvidedEffectKeys)
                {
                    providedKeys.Add(key);
                }

                var offHandComplementBonus = GetVariantOffHandComplementBonus(
                    baselineProvidedKeys,
                    off.ProvidedEffectKeys,
                    GetDetectedEffectsForSlot(main.Slot, request),
                    GetDetectedEffectsForSlot(off.Slot, request),
                    role,
                    request);
                characterScore += offHandComplementBonus;
            }

            var effectiveSubWeaponRoleInference = InferEffectiveSubWeaponRole(
                role,
                main.Slot,
                off?.Slot,
                ultimate?.Slot,
                mainCostume?.Slot,
                providedKeys,
                passivePoints,
                request);

            var candidate = new CharacterBuildCandidate
            {
                CharacterName = character,
                Role = role,
                EffectiveSubWeaponRole = effectiveSubWeaponRoleInference.Role,
                EffectiveSubWeaponRoleReason = effectiveSubWeaponRoleInference.Reason,
                BaseScore = characterScore,
                NonPassiveScore = nonPassiveScore,
                ScoreBreakdown = new List<PlayerPowerAnalyzerV2ScoreComponent>
                {
                    CreateScoreComponent("character", "Base non-passive score", nonPassiveScore),
                    CreateScoreComponent("character", "Passive score", passiveScore)
                },
                MainWeapon = main.Slot,
                MainPassivePoints = new Dictionary<string, int>(main.PassivePoints, StringComparer.OrdinalIgnoreCase),
                OffHandWeapon = off?.Slot,
                OffPassivePoints = off == null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, int>(off.PassivePoints, StringComparer.OrdinalIgnoreCase),
                UltimateWeapon = ultimate?.Slot,
                UltimatePassivePoints = ultimate == null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, int>(ultimate.PassivePoints, StringComparer.OrdinalIgnoreCase),
                MainOutfit = mainCostume?.Slot,
                MainOutfitPassivePoints = mainCostume == null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, int>(mainCostume.PassivePoints, StringComparer.OrdinalIgnoreCase),
                SubOutfits = subOutfits.ToList(),
                SubOutfitPassivePoints = new Dictionary<string, int>(subOutfitPassivePoints, StringComparer.OrdinalIgnoreCase),
                ProvidedEffectKeys = providedKeys,
                UsedItemNames = new HashSet<string>(usedNames, StringComparer.OrdinalIgnoreCase),
                PassivePoints = passivePoints,
                ActiveFamilyContributions = activeFamilyContributions
            };

            var offensiveRoleInference = EnsureOffensiveRoleInference(candidate, request);
            candidate.OffensiveRole = offensiveRoleInference.Role;
            candidate.OffensiveRoleReason = offensiveRoleInference.Reason;
            candidate.EstimatedDirectOffenseScore = offensiveRoleInference.DirectPressure;
            candidate.EstimatedOffensiveSupportScore = offensiveRoleInference.EnablerPressure;
            candidate.EstimatedLowActualUsePenalty = offensiveRoleInference.LowActualUsePenalty;
            return candidate;
        }

        private List<TeamCandidate> BuildTeamCandidates(
            IReadOnlyDictionary<string, List<CharacterBuildCandidate>> variantsByCharacter,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            IReadOnlyList<HashSet<string>> mutuallyExclusiveCharacterGroups,
            ConcurrentDictionary<string, SlotEvaluation> weaponSlotEvaluationCache)
        {
            var characters = variantsByCharacter
                .Where(kvp => kvp.Value.Count > 0)
                .Select(kvp => kvp.Key)
                .OrderBy(character => character, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var teamCombinations = new List<string[]>();
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

                        teamCombinations.Add(teamCharacters);
                    }
                }
            }

            var teamCandidates = new ConcurrentBag<TeamCandidate>();
            Parallel.ForEach(teamCombinations, CreateCpuBoundParallelOptions(), teamCharacters =>
            {
                foreach (var candidate in BuildTeamCandidatesForCharacters(teamCharacters, variantsByCharacter, ownedWeapons, request, referenceTuningProfile, normalizedEnabledTemplateNames, weaponSlotEvaluationCache))
                {
                    teamCandidates.Add(candidate);
                }
            });

            return OrderTeamCandidatesForSelection(teamCandidates).ToList();
        }

        private List<TeamCandidate> BuildTeamCandidatesForCharacters(
            string[] teamCharacters,
            IReadOnlyDictionary<string, List<CharacterBuildCandidate>> variantsByCharacter,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            ConcurrentDictionary<string, SlotEvaluation> weaponSlotEvaluationCache)
        {
            var charA = variantsByCharacter[teamCharacters[0]];
            var charB = variantsByCharacter[teamCharacters[1]];
            var charC = variantsByCharacter[teamCharacters[2]];
            var optimisticSubWeaponGainsByCharacter = BuildOptimisticSubWeaponGainsForTeam(teamCharacters, ownedWeapons, request, referenceTuningProfile, weaponSlotEvaluationCache);
            TeamCandidate? bestTeamCandidate = null;
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

                        var baseVariants = new[] { a, b, c };
                        var providedEffectKeys = GetProvidedEffectKeys(baseVariants);
                        if (!CanSatisfyHardRequirements(providedEffectKeys, request))
                        {
                            continue;
                        }

                        if (bestTeamCandidate != null)
                        {
                            var optimisticScoreCeiling = EstimateTeamCandidateScoreCeiling(
                                baseVariants,
                                providedEffectKeys,
                                optimisticSubWeaponGainsByCharacter,
                                request,
                                referenceTuningProfile,
                                normalizedEnabledTemplateNames);
                            if (optimisticScoreCeiling <= bestTeamCandidate.Score + 0.001)
                            {
                                continue;
                            }
                        }

                        var team = FinalizeTeamCandidate(baseVariants, ownedWeapons, request, referenceTuningProfile, normalizedEnabledTemplateNames, weaponSlotEvaluationCache);
                        if (team != null)
                        {
                            if (bestTeamCandidate == null || IsPreferredTeamCandidate(team, bestTeamCandidate))
                            {
                                bestTeamCandidate = team;
                            }
                        }
                    }
                }
            }

            return bestTeamCandidate == null
                ? new List<TeamCandidate>()
                : new List<TeamCandidate> { bestTeamCandidate };
        }

        private Dictionary<string, List<OptimisticSubWeaponGain>> BuildOptimisticSubWeaponGainsForTeam(
            IReadOnlyList<string> teamCharacters,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            ConcurrentDictionary<string, SlotEvaluation> weaponSlotEvaluationCache)
        {
            var nonUltimateWeapons = ownedWeapons
                .Where(weapon => !weapon.IsUltimate)
                .OrderBy(weapon => weapon.Item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var lookup = new Dictionary<string, List<OptimisticSubWeaponGain>>(StringComparer.OrdinalIgnoreCase);
            foreach (var teamCharacter in teamCharacters)
            {
                var gains = new List<OptimisticSubWeaponGain>();
                foreach (var weapon in nonUltimateWeapons)
                {
                    var bestGainForCharacter = new OptimisticSubWeaponGain
                    {
                        WeaponName = weapon.Item.Name,
                        Gain = double.MinValue,
                        EvaluationScore = double.MinValue
                    };

                    foreach (var role in new[] { CharacterRole.DPS, CharacterRole.Support, CharacterRole.Healer, CharacterRole.Tank })
                    {
                        var evaluation = GetOrCreateWeaponSlot(weapon, role, request, referenceTuningProfile, "Sub Weapon", 0.5, false, false, weaponSlotEvaluationCache);
                        var optimisticGain = GetOptimisticSubWeaponGainUpperBound(evaluation.PassivePoints, evaluation.NonPassiveScore, role, teamCharacters.Count, request);
                        if (optimisticGain > bestGainForCharacter.Gain + 0.001
                            || (Math.Abs(optimisticGain - bestGainForCharacter.Gain) <= 0.001 && evaluation.Score > bestGainForCharacter.EvaluationScore))
                        {
                            bestGainForCharacter = new OptimisticSubWeaponGain
                            {
                                WeaponName = evaluation.Name,
                                Gain = optimisticGain,
                                EvaluationScore = evaluation.Score
                            };
                        }
                    }

                    gains.Add(bestGainForCharacter);
                }

                lookup[teamCharacter] = gains
                    .OrderByDescending(entry => entry.Gain)
                    .ThenByDescending(entry => entry.EvaluationScore)
                    .ThenBy(entry => entry.WeaponName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return lookup;
        }

        private double EstimateTeamCandidateScoreCeiling(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            HashSet<string> providedEffectKeys,
            IReadOnlyDictionary<string, List<OptimisticSubWeaponGain>> optimisticSubWeaponGainsByCharacter,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames)
        {
            var usedItemNames = new HashSet<string>(baseVariants.SelectMany(variant => variant.UsedItemNames), StringComparer.OrdinalIgnoreCase);
            var scoreWithoutSubWeapons = ComputeTeamScoreWithoutSubWeapons(baseVariants, providedEffectKeys, request, referenceTuningProfile, normalizedEnabledTemplateNames);
            var optimisticSubWeaponGain = 0d;
            foreach (var variant in baseVariants)
            {
                if (!optimisticSubWeaponGainsByCharacter.TryGetValue(variant.CharacterName, out var gains))
                {
                    continue;
                }

                var equippedCount = 0;
                foreach (var gain in gains)
                {
                    if (usedItemNames.Contains(gain.WeaponName))
                    {
                        continue;
                    }

                    optimisticSubWeaponGain += gain.Gain;
                    equippedCount++;
                    if (equippedCount >= 3)
                    {
                        break;
                    }
                }
            }

            return scoreWithoutSubWeapons + optimisticSubWeaponGain;
        }

        private static double GetOptimisticSubWeaponGainUpperBound(
            IReadOnlyDictionary<string, int> weaponPassivePoints,
            double nonPassiveScore,
            CharacterRole equippingRole,
            int teamCharacterCount,
            PlayerPowerAnalyzerV2Request request)
        {
            var gain = nonPassiveScore;
            foreach (var kvp in weaponPassivePoints)
            {
                if (kvp.Value <= 0)
                {
                    continue;
                }

                if (IsTeamWidePassive(kvp.Key))
                {
                    var maxRecipientWeight = teamCharacterCount <= 0
                        ? 0d
                        : new[] { CharacterRole.DPS, CharacterRole.Support, CharacterRole.Healer, CharacterRole.Tank }
                            .Max(role => ScoreTeamWidePassiveSkillForRecipient(kvp.Key, kvp.Value, role, request));
                    gain += maxRecipientWeight * Math.Min(3, teamCharacterCount);
                    continue;
                }

                gain += ScorePassiveSkill(kvp.Key, kvp.Value, equippingRole, request);
                gain += GetOptimisticMaintenancePassiveUpperBound(kvp.Key, kvp.Value, equippingRole, request);
            }

            return gain;
        }

        private double ComputeTeamScoreWithoutSubWeapons(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            HashSet<string> providedEffectKeys,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames)
        {
            var passivePointsByCharacter = baseVariants.ToDictionary(
                variant => variant.CharacterName,
                variant => new Dictionary<string, int>(variant.PassivePoints, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            var offensiveBackbone = 0d;
            foreach (var variant in baseVariants)
            {
                offensiveBackbone += variant.NonPassiveScore + ScoreCharacterPassivePoints(passivePointsByCharacter[variant.CharacterName], passivePointsByCharacter.Values, variant.Role, request);
            }

            var explicitDetectedEffects = baseVariants.SelectMany(variant => GetDetectedEffectsForVariant(variant, request)).ToList();
            var effectPackage = ScoreEffectPackage(explicitDetectedEffects, baseVariants, request);
            var anchorSupportScore = ScoreAnchorSupportSynergy(baseVariants, explicitDetectedEffects, request);
            var redundantOffHandPenalty = GetFinalTeamRedundantOffHandPenalty(baseVariants, request);
            var variantAlignmentPenalty = GetFinalTeamVariantAlignmentPenalty(baseVariants, request);
            var teamWideDuplicateBuffPenalty = GetTeamWideDuplicateBuffPenalty(baseVariants);
            var contextualVariantBonus = GetVariantContextualTeamBonus(baseVariants, request);
            var offensiveShellScore = ScoreOffensiveShell(baseVariants, request);

            // Mirror FinalizeTeamCandidate so the pruning ceiling stays on the same scale.
            offensiveBackbone += effectPackage.Score
                + anchorSupportScore.Score
                + ScoreTeamEffects(providedEffectKeys, request) * 0.18
                + offensiveShellScore.Score * 0.65
                + ScoreReferencePatternSynergyBonus(baseVariants, providedEffectKeys, request, referenceTuningProfile);
            var refinementTerms = redundantOffHandPenalty
                - variantAlignmentPenalty
                - teamWideDuplicateBuffPenalty
                + contextualVariantBonus
                + request.SoftPreferredEffectKeys.Count(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) * 90
                + request.HardRequiredEffectKeys.Count * 40;

            var score = (request.PreferredDamageType != DamageType.Any
                ? EstimateTeamDamage(baseVariants, request)
                : offensiveBackbone) + refinementTerms;

            var roles = baseVariants
                .Select(variant => variant.Role.ToString())
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var teamRoleKey = NormalizeTemplateName(string.Join("/", roles));
            if (!normalizedEnabledTemplateNames.ContainsKey(teamRoleKey))
            {
                score *= 0.5;
            }

            return score;
        }

        private static HashSet<string> GetProvidedEffectKeys(IEnumerable<CharacterBuildCandidate> variants)
        {
            return new HashSet<string>(variants.SelectMany(variant => variant.ProvidedEffectKeys), StringComparer.OrdinalIgnoreCase);
        }

        private static bool CanSatisfyHardRequirements(IReadOnlyCollection<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request)
        {
            return request.HardRequiredEffectKeys.All(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
        }

        private static bool IsPreferredTeamCandidate(TeamCandidate candidate, TeamCandidate incumbent)
        {
            return OrderTeamCandidatesForSelection(new[] { candidate, incumbent }).First() == candidate;
        }

        private TeamCandidate? FinalizeTeamCandidate(
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            List<OwnedWeaponCandidate> ownedWeapons,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            IReadOnlyDictionary<string, string> normalizedEnabledTemplateNames,
            ConcurrentDictionary<string, SlotEvaluation> weaponSlotEvaluationCache)
        {
            var usedItemNames = new HashSet<string>(baseVariants.SelectMany(v => v.UsedItemNames), StringComparer.OrdinalIgnoreCase);
            var characterOutputs = baseVariants.Select(v => v.ToOutput()).ToList();
            var characterOutputsByName = characterOutputs.ToDictionary(c => c.CharacterName, StringComparer.OrdinalIgnoreCase);
            var baseNonPassiveScoresByCharacter = baseVariants.ToDictionary(v => v.CharacterName, v => v.NonPassiveScore, StringComparer.OrdinalIgnoreCase);
            var hasRequestedElementMainOrOffHandByCharacter = baseVariants.ToDictionary(v => v.CharacterName, v => HasRequestedElementMainOrOffHand(v, request), StringComparer.OrdinalIgnoreCase);
            var passivePointsByCharacter = baseVariants.ToDictionary(v => v.CharacterName, v => new Dictionary<string, int>(v.PassivePoints, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            var subWeaponNonPassiveScoreByCharacter = baseVariants.ToDictionary(v => v.CharacterName, _ => 0d, StringComparer.OrdinalIgnoreCase);
            var availableSubWeapons = ownedWeapons
                .Where(w => !w.IsUltimate && !usedItemNames.Contains(w.Item.Name))
                .OrderBy(w => w.Item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var assignments = new List<SubWeaponAssignment>();
            var providedEffectKeysByCharacter = baseVariants.ToDictionary(
                variant => variant.CharacterName,
                variant => (IReadOnlyCollection<string>)variant.ProvidedEffectKeys.ToList(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var character in baseVariants)
            {
                foreach (var weapon in availableSubWeapons)
                {
                    var evaluation = GetOrCreateWeaponSlot(weapon, character.EffectiveSubWeaponRole, request, referenceTuningProfile, "Sub Weapon", 0.5, false, false, weaponSlotEvaluationCache);
                    assignments.Add(new SubWeaponAssignment
                    {
                        CharacterName = character.CharacterName,
                        Evaluation = evaluation
                    });
                }
            }

            var assignedCounts = baseVariants.ToDictionary(v => v.CharacterName, _ => 0, StringComparer.OrdinalIgnoreCase);
            var teamRoles = characterOutputs.Select(character => character.Role).ToList();
            var anchorCharacterName = BuildAnchorScoringContext(baseVariants, request)?.CharacterName;
            var anchorRole = anchorCharacterName == null
                ? (CharacterRole?)null
                : baseVariants.FirstOrDefault(variant => variant.CharacterName.Equals(anchorCharacterName, StringComparison.OrdinalIgnoreCase))?.Role;
            while (true)
            {
                var currentTeamWidePassivePoints = AggregateTeamWidePassivePoints(passivePointsByCharacter.Values);
                SubWeaponAssignment? bestAssignment = null;
                var bestGain = double.MinValue;

                foreach (var assignment in assignments)
                {
                    if (assignedCounts[assignment.CharacterName] >= 3)
                    {
                        continue;
                    }

                    if (usedItemNames.Contains(assignment.Evaluation.Name))
                    {
                        continue;
                    }

                    var characterRole = characterOutputsByName[assignment.CharacterName].EffectiveSubWeaponRole;
                    var gain = anchorRole.HasValue && !string.IsNullOrWhiteSpace(anchorCharacterName)
                        ? GetSubWeaponMarginalGainWithAnchorContext(
                            assignment.Evaluation.PassivePoints,
                            assignment.Evaluation.NonPassiveScore,
                            assignment.Evaluation.BattleFitMultiplier,
                            passivePointsByCharacter[assignment.CharacterName],
                            currentTeamWidePassivePoints,
                            characterRole,
                            teamRoles,
                            providedEffectKeysByCharacter[assignment.CharacterName],
                            assignment.CharacterName,
                            anchorCharacterName,
                            anchorRole.Value,
                            hasRequestedElementMainOrOffHandByCharacter[assignment.CharacterName],
                            request)
                        : GetSubWeaponMarginalGain(
                            assignment.Evaluation.PassivePoints,
                            assignment.Evaluation.NonPassiveScore,
                            assignment.Evaluation.BattleFitMultiplier,
                            passivePointsByCharacter[assignment.CharacterName],
                            currentTeamWidePassivePoints,
                            characterRole,
                            teamRoles,
                            providedEffectKeysByCharacter[assignment.CharacterName],
                            hasRequestedElementMainOrOffHandByCharacter[assignment.CharacterName],
                            request);

                    if (gain > bestGain + 0.001
                        || (Math.Abs(gain - bestGain) <= 0.001
                            && (bestAssignment == null
                                || assignment.Evaluation.Score > bestAssignment.Evaluation.Score + 0.001
                                || (Math.Abs(assignment.Evaluation.Score - bestAssignment.Evaluation.Score) <= 0.001
                                    && string.Compare(assignment.CharacterName, bestAssignment.CharacterName, StringComparison.OrdinalIgnoreCase) < 0)
                                || (Math.Abs(assignment.Evaluation.Score - bestAssignment.Evaluation.Score) <= 0.001
                                    && string.Equals(assignment.CharacterName, bestAssignment.CharacterName, StringComparison.OrdinalIgnoreCase)
                                    && string.Compare(assignment.Evaluation.Name, bestAssignment.Evaluation.Name, StringComparison.OrdinalIgnoreCase) < 0))))
                    {
                        bestAssignment = assignment;
                        bestGain = gain;
                    }
                }

                if (bestAssignment == null)
                {
                    break;
                }

                if (!usedItemNames.Add(bestAssignment.Evaluation.Name))
                {
                    continue;
                }

                var character = characterOutputsByName[bestAssignment.CharacterName];
                character.SubWeapons.Add(bestAssignment.Evaluation.Slot);
                character.TotalPatk += bestAssignment.Evaluation.Slot.Patk;
                character.TotalMatk += bestAssignment.Evaluation.Slot.Matk;
                character.TotalHeal += bestAssignment.Evaluation.Slot.Heal;
                assignedCounts[bestAssignment.CharacterName]++;
                subWeaponNonPassiveScoreByCharacter[bestAssignment.CharacterName] += bestAssignment.Evaluation.NonPassiveScore;
                AddPassivePoints(passivePointsByCharacter[bestAssignment.CharacterName], bestAssignment.Evaluation.PassivePoints);
            }

            foreach (var character in characterOutputs)
            {
                var finalPassiveScore = ScoreCharacterPassivePoints(passivePointsByCharacter[character.CharacterName], passivePointsByCharacter.Values, character.Role, request);
                character.Score = Math.Round(
                    baseNonPassiveScoresByCharacter[character.CharacterName]
                    + subWeaponNonPassiveScoreByCharacter[character.CharacterName]
                    + finalPassiveScore,
                    2);
                character.ScoreBreakdown = new List<PlayerPowerAnalyzerV2ScoreComponent>
                {
                    CreateScoreComponent("character", "Base non-passive score", baseNonPassiveScoresByCharacter[character.CharacterName]),
                    CreateScoreComponent("sub_weapons", "Sub-weapon non-passive gain", subWeaponNonPassiveScoreByCharacter[character.CharacterName]),
                    CreateScoreComponent("passives", "Final passive score", finalPassiveScore)
                };
                character.KeyRAbilities = BuildCharacterKeyRAbilities(character.CharacterName, passivePointsByCharacter);
            }

            var providedEffectKeys = new HashSet<string>(baseVariants.SelectMany(v => v.ProvidedEffectKeys), StringComparer.OrdinalIgnoreCase);
            ApplyImplicitMateriaRecommendations(characterOutputs, providedEffectKeys, request);
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
            var explicitDetectedEffects = baseVariants.SelectMany(v => GetDetectedEffectsForVariant(v, request)).ToList();
            var offensiveAbilitySummary = BuildOffensiveAbilitySummary(explicitDetectedEffects, request);
            var effectPackage = ScoreEffectPackage(explicitDetectedEffects, baseVariants, request);
            var anchorSupportScore = ScoreAnchorSupportSynergy(baseVariants, explicitDetectedEffects, request);
            var legacyTeamEffectScore = ScoreTeamEffects(providedEffectKeys, request) * 0.18;
            var redundantOffHandPenalty = GetFinalTeamRedundantOffHandPenalty(baseVariants, request);
            var variantAlignmentPenalty = GetFinalTeamVariantAlignmentPenalty(baseVariants, request);
            var teamWideDuplicateBuffPenalty = GetTeamWideDuplicateBuffPenalty(baseVariants);
            var contextualVariantBonus = GetVariantContextualTeamBonus(baseVariants, request);
            var offensiveShellScore = ScoreOffensiveShell(baseVariants, request);
            var referencePatternBonus = ScoreReferencePatternSynergyBonus(baseVariants, providedEffectKeys, request, referenceTuningProfile);

            // Item 3: offensive builds rank by estimated team damage (the real multiplicative model); the
            // legacy offensive backbone is replaced. Refinement terms (dedup penalty, requirement bonuses,
            // alignment penalties) stay at full weight as tie-breakers.
            var offensiveBackbone = characterOutputs.Sum(c => c.Score)
                + effectPackage.Score + legacyTeamEffectScore + anchorSupportScore.Score
                + offensiveShellScore.Score * 0.75
                + referencePatternBonus;
            var refinementTerms = redundantOffHandPenalty
                - variantAlignmentPenalty
                - teamWideDuplicateBuffPenalty
                + contextualVariantBonus
                + matchedPreferred.Count * 90
                + request.HardRequiredEffectKeys.Count * 40;

            var score = (request.PreferredDamageType != DamageType.Any
                ? EstimateTeamDamage(baseVariants, request)
                : offensiveBackbone) + refinementTerms;

            var roles = characterOutputs.Select(c => c.Role.ToString()).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
            var teamRoleKey = NormalizeTemplateName(string.Join("/", roles));
            normalizedEnabledTemplateNames.TryGetValue(teamRoleKey, out var matchedTemplateName);
            var teamScoreBreakdown = new List<PlayerPowerAnalyzerV2ScoreComponent>();
            foreach (var character in characterOutputs)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("character_total", character.CharacterName, character.Score));
            }

            var teamEffectScore = effectPackage.Score + legacyTeamEffectScore;
            if (teamEffectScore != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("team_effects", "Provided team effects", teamEffectScore));
            }

            if (anchorSupportScore.Score != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("anchor", "Anchor-DPS support synergy", anchorSupportScore.Score));
            }

            if (redundantOffHandPenalty != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("loadout", "Redundant same-character off-hand attack buff", redundantOffHandPenalty));
            }

            if (variantAlignmentPenalty != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("loadout", "Variant alignment penalty", -variantAlignmentPenalty));
            }

            if (teamWideDuplicateBuffPenalty != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("loadout", "Duplicate team buff (shared cap)", -teamWideDuplicateBuffPenalty));
            }

            if (contextualVariantBonus != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("loadout", "Limited-use Gear C ability support", contextualVariantBonus));
            }

            if (offensiveShellScore.Score != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("offensive_shell", "Offensive shell coherence", offensiveShellScore.Score * 0.75));
            }

            var matchedPreferredBonus = matchedPreferred.Count * 90;
            if (matchedPreferredBonus != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("preferences", "Matched preferred effects", matchedPreferredBonus));
            }

            var hardRequirementBonus = request.HardRequiredEffectKeys.Count * 40;
            if (hardRequirementBonus != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("requirements", "Hard required effect coverage", hardRequirementBonus));
            }

            if (referencePatternBonus != 0)
            {
                teamScoreBreakdown.Add(CreateScoreComponent("reference", "Reference-informed tuning bonus", referencePatternBonus));
            }

            if (string.IsNullOrWhiteSpace(matchedTemplateName))
            {
                var penalizedScore = score * 0.5;
                teamScoreBreakdown.Add(CreateScoreComponent("template", "No enabled team template matched", penalizedScore - score));
                score *= 0.5;
            }

            var debugNotes = new List<string>
            {
                $"Roles: {string.Join(", ", roles)}",
                $"Effective sub-weapon roles: {string.Join(", ", characterOutputs.OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase).Select(character => $"{character.CharacterName}={character.EffectiveSubWeaponRole}"))}",
                $"Offensive roles: {string.Join(", ", baseVariants.OrderBy(variant => variant.CharacterName, StringComparer.OrdinalIgnoreCase).Select(variant => $"{variant.CharacterName}={EnsureOffensiveRoleInference(variant, request).Role}"))}",
                string.IsNullOrWhiteSpace(matchedTemplateName) ? "No enabled team template matched (50% penalty applied)." : $"Matched team template: {matchedTemplateName}"
            };
            if (referencePatternBonus > 0)
            {
                debugNotes.Add($"Reference-informed tuning bonus applied: +{referencePatternBonus:0.##}.");
            }

            if (offensiveShellScore.Notes.Count > 0)
            {
                debugNotes.AddRange(offensiveShellScore.Notes);
            }

            if (effectPackage.Notes.Count > 0)
            {
                debugNotes.AddRange(effectPackage.Notes);
            }

            AppendAnchorScoringDebugNotes(debugNotes, anchorSupportScore);
            AppendReferenceMatchDebugNotes(debugNotes, request, characterOutputs, providedEffectKeys);
            AppendImplicitSetupDebugNotes(debugNotes, baseVariants, providedEffectKeys, request);
            var orderedCharacters = characterOutputs
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new TeamCandidate
            {
                Characters = orderedCharacters,
                Score = score,
                TemplateName = matchedTemplateName,
                OffensiveAbilitySummary = offensiveAbilitySummary,
                MatchedRequiredEffects = request.HardRequiredEffectKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                MissingRequiredEffects = missingRequired,
                MatchedPreferredEffects = matchedPreferred,
                MissingPreferredEffects = missingPreferred,
                ProvidedEffectKeys = providedEffectKeys.ToList(),
                SuppressedEffectNotes = BuildSuppressedEffectNotes(request.BossImmunityKeys),
                DebugNotes = debugNotes,
                ScoreBreakdown = teamScoreBreakdown,
                TeamKey = string.Join("|", orderedCharacters.Select(c => c.CharacterName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                EquipmentKey = BuildTeamEquipmentKey(orderedCharacters)
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

        private static void AppendImplicitSetupDebugNotes(
            List<string> debugNotes,
            IReadOnlyList<CharacterBuildCandidate> baseVariants,
            IEnumerable<string> providedEffectKeys,
            PlayerPowerAnalyzerV2Request request)
        {
            if (request.EnemyWeakness == Element.None)
            {
                return;
            }

            if (!providedEffectKeys.Contains("elemental_resistance_down", StringComparer.OrdinalIgnoreCase))
            {
                var note = $"No explicit {request.EnemyWeakness} Resistance Down source is equipped.";
                if (CanAssumeStandardSynthDebuffSeedSetup(baseVariants))
                {
                    note += " Recommendation likely expects synth materia or equivalent setup coverage.";
                }

                debugNotes.Add(note);
            }
        }

        private static void ApplyImplicitMateriaRecommendations(
            IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characterOutputs,
            IReadOnlyCollection<string> providedEffectKeys,
            PlayerPowerAnalyzerV2Request request)
        {
            if (characterOutputs.Count == 0 || !CanAssignImplicitMateriaRecommendations(characterOutputs))
            {
                return;
            }

            var targetCharacter = SelectImplicitMateriaTarget(characterOutputs);
            if (targetCharacter == null)
            {
                return;
            }

            var recommendations = BuildImplicitMateriaRecommendations(providedEffectKeys, request);
            if (recommendations.Count == 0)
            {
                return;
            }

            targetCharacter.RecommendedMateria = recommendations;
        }

        private static bool CanAssignImplicitMateriaRecommendations(IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characterOutputs)
        {
            return characterOutputs.Any(character => character.Role is CharacterRole.Support or CharacterRole.Healer);
        }

        private static PlayerPowerAnalyzerV2CharacterBuild? SelectImplicitMateriaTarget(IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characterOutputs)
        {
            return characterOutputs
                .OrderBy(character => character.Role switch
                {
                    CharacterRole.Support => 0,
                    CharacterRole.Healer => 1,
                    CharacterRole.Tank => 2,
                    CharacterRole.DPS => 3,
                    _ => 4
                })
                .ThenByDescending(character => character.Score)
                .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static List<PlayerPowerAnalyzerV2MateriaRecommendation> BuildImplicitMateriaRecommendations(
            IReadOnlyCollection<string> providedEffectKeys,
            PlayerPowerAnalyzerV2Request request)
        {
            var recommendations = new List<PlayerPowerAnalyzerV2MateriaRecommendation>();
            var slotNumber = 1;

            if (request.EnemyWeakness != Element.None
                && !providedEffectKeys.Contains("elemental_resistance_down", StringComparer.OrdinalIgnoreCase))
            {
                recommendations.Add(new PlayerPowerAnalyzerV2MateriaRecommendation
                {
                    SlotNumber = slotNumber++,
                    Name = $"{request.EnemyWeakness} Breach Materia",
                    ProvidedEffectLabel = $"{request.EnemyWeakness} Resistance Down",
                    Reason = $"No explicit {request.EnemyWeakness} Resistance Down source is equipped.",
                    IsAssumed = true
                });
            }

            if (request.PreferredDamageType == DamageType.Physical)
            {
                if (!providedEffectKeys.Contains("pdef_down", StringComparer.OrdinalIgnoreCase))
                {
                    recommendations.Add(new PlayerPowerAnalyzerV2MateriaRecommendation
                    {
                        SlotNumber = slotNumber++,
                        Name = "Breach Materia",
                        ProvidedEffectLabel = "PDEF Down",
                        Reason = "No explicit PDEF Down source is equipped.",
                        IsAssumed = true
                    });
                }

                if (!providedEffectKeys.Contains("patk_up", StringComparer.OrdinalIgnoreCase))
                {
                    recommendations.Add(new PlayerPowerAnalyzerV2MateriaRecommendation
                    {
                        SlotNumber = slotNumber++,
                        Name = "Bravery Materia",
                        ProvidedEffectLabel = "PATK Up",
                        Reason = "No explicit PATK Up source is equipped.",
                        IsAssumed = true
                    });
                }
            }
            else if (request.PreferredDamageType == DamageType.Magical)
            {
                if (!providedEffectKeys.Contains("mdef_down", StringComparer.OrdinalIgnoreCase))
                {
                    recommendations.Add(new PlayerPowerAnalyzerV2MateriaRecommendation
                    {
                        SlotNumber = slotNumber++,
                        Name = "Mana Breach Materia",
                        ProvidedEffectLabel = "MDEF Down",
                        Reason = "No explicit MDEF Down source is equipped.",
                        IsAssumed = true
                    });
                }

                if (!providedEffectKeys.Contains("matk_up", StringComparer.OrdinalIgnoreCase))
                {
                    recommendations.Add(new PlayerPowerAnalyzerV2MateriaRecommendation
                    {
                        SlotNumber = slotNumber++,
                        Name = "Faith Materia",
                        ProvidedEffectLabel = "MATK Up",
                        Reason = "No explicit MATK Up source is equipped.",
                        IsAssumed = true
                    });
                }
            }

            return recommendations
                .Take(3)
                .ToList();
        }

        private static IOrderedEnumerable<CharacterBuildCandidate> OrderCharacterVariantsForSelection(
            IEnumerable<CharacterBuildCandidate> variants,
            PlayerPowerAnalyzerV2Request request)
        {
            return variants
                .OrderByDescending(variant => GetVariantSelectionScore(variant, request))
                .ThenBy(BuildCharacterVariantEquipmentKey, StringComparer.OrdinalIgnoreCase);
        }

        private static IOrderedEnumerable<TeamCandidate> OrderTeamCandidatesForSelection(IEnumerable<TeamCandidate> candidates)
        {
            return candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.TemplateName))
                .ThenBy(candidate => candidate.TeamKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.EquipmentKey, StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildCharacterVariantEquipmentKey(CharacterBuildCandidate variant)
        {
            return string.Join("|", new[]
            {
                variant.CharacterName,
                variant.MainWeapon.Name,
                variant.OffHandWeapon?.Name ?? string.Empty,
                variant.UltimateWeapon?.Name ?? string.Empty,
                variant.MainOutfit?.Name ?? string.Empty,
                string.Join(">", variant.SubOutfits.Select(slot => slot.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            });
        }

        private static string BuildTeamEquipmentKey(IReadOnlyCollection<PlayerPowerAnalyzerV2CharacterBuild> characters)
        {
            return string.Join("||", characters
                .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                .Select(BuildCharacterOutputEquipmentKey));
        }

        private static string BuildCharacterOutputEquipmentKey(PlayerPowerAnalyzerV2CharacterBuild character)
        {
            return string.Join("|", new[]
            {
                character.CharacterName,
                character.MainWeapon?.Name ?? string.Empty,
                character.OffHandWeapon?.Name ?? string.Empty,
                character.UltimateWeapon?.Name ?? string.Empty,
                character.MainOutfit?.Name ?? string.Empty,
                string.Join(">", character.SubWeapons.Select(slot => slot.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)),
                string.Join(">", character.SubOutfits.Select(slot => slot.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            });
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
            ConcurrentDictionary<string, SlotEvaluation> cache)
        {
            var cacheKey = string.Join("|", weapon.Item.Id, role, slotName, slotMultiplier, includeActiveEffects, includeDamage);
            return cache.GetOrAdd(cacheKey, _ => CreateWeaponSlot(weapon, role, request, referenceTuningProfile, slotName, slotMultiplier, includeActiveEffects, includeDamage));
        }

        private SlotEvaluation GetOrCreateCostumeSlot(
            OwnedCostumeCandidate costume,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            string slotName,
            double slotMultiplier,
            bool includeActiveEffects,
            ConcurrentDictionary<string, SlotEvaluation> cache)
        {
            var cacheKey = string.Join("|", costume.Item.Id, role, slotName, slotMultiplier, includeActiveEffects);
            return cache.GetOrAdd(cacheKey, _ => CreateCostumeSlot(costume, role, request, referenceTuningProfile, slotName, slotMultiplier, includeActiveEffects));
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
                .Select(kvp => $"{FormatPassiveDisplayLabel(kvp.Key)} +{kvp.Value} pts")
                .ToList();
            var selectedCustomization = SelectBestCustomization(weapon, role, request, referenceTuningProfile, slotName, slotMultiplier, includeActiveEffects);
            if (selectedCustomization.PassiveSkillName != null && selectedCustomization.PassiveSkillPoints > 0)
            {
                var appliedPoints = Math.Max(0, (int)Math.Floor(selectedCustomization.PassiveSkillPoints * slotMultiplier));
                if (appliedPoints > 0)
                {
                    AddPassivePointValues(passivePoints, selectedCustomization.PassiveSkillName, appliedPoints, selectedCustomization.PassiveEffects);
                    passiveSummaries = passivePoints
                        .Where(kvp => kvp.Value > 0)
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => $"{FormatPassiveDisplayLabel(kvp.Key)} +{kvp.Value} pts")
                        .ToList();
                }
            }

            var patk = Math.Max(0, (int)Math.Floor(weapon.Snapshot.Patk * slotMultiplier));
            var matk = Math.Max(0, (int)Math.Floor(weapon.Snapshot.Matk * slotMultiplier));
            var heal = Math.Max(0, (int)Math.Floor(weapon.Snapshot.Heal * slotMultiplier));
            var statScore = ScoreStats(patk, matk, heal, role, request);
            var isOffHandSlot = slotName.Equals("Off-hand", StringComparison.OrdinalIgnoreCase);
            var passiveScore = ScorePassivePoints(passivePoints, role, request, isOffHandSlot);
            var scoreBreakdown = new List<PlayerPowerAnalyzerV2ScoreComponent>
            {
                CreateScoreComponent("stats", "Base stats", statScore),
                CreateScoreComponent("passives", "Passive score", passiveScore)
            };
            var nonPassiveScore = statScore;
            var score = nonPassiveScore + passiveScore;
            var providedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var detectedEffects = new List<DetectedActiveEffect>();
            var damageScore = 0d;
            if (includeDamage)
            {
                damageScore = ScoreDamageWithReferenceTuning(weapon, role, request, referenceTuningProfile);
                nonPassiveScore += damageScore;
                score += damageScore;
                scoreBreakdown.Add(CreateScoreComponent("damage", "Active damage", damageScore));
            }

            var rawActiveUtilityScore = 0d;
            var activeUtilityByFamily = new Dictionary<string, SlotActiveFamilyContribution>(StringComparer.OrdinalIgnoreCase);
            if (includeActiveEffects)
            {
                var atbAdjustment = 0d;
                var weaponEffects = DetectActiveEffects(weapon.Item.EffectTags, weapon.Snapshot.AbilityText, request, request.BossImmunityKeys, slotName, weapon.Item.Name, weapon.Item.AbilityType, weapon.Item.Element);
                detectedEffects.AddRange(weaponEffects);
                var actionHasThresholdSensitiveSetup = HasThresholdSensitiveSetupEffect(weaponEffects);
                foreach (var effect in weaponEffects)
                {
                    providedKeys.Add(effect.Key);
                    var rawEffectScore = ScoreActiveEffectWithReferenceTuning(effect, role, request, referenceTuningProfile);
                    var effectScore = ApplyAtbEfficiencyToUtilityScore(rawEffectScore, weapon.Item.CommandAtb, effect, role, slotName, actionHasThresholdSensitiveSetup);
                    rawActiveUtilityScore += effectScore;
                    atbAdjustment += effectScore - rawEffectScore;
                    nonPassiveScore += effectScore;
                    score += effectScore;
                    AccumulateActiveFamilyContribution(activeUtilityByFamily, effect, effectScore, role);
                    scoreBreakdown.Add(CreateScoreComponent("effects", ToLabel(effect.Key), rawEffectScore));
                }

                if (!string.IsNullOrWhiteSpace(selectedCustomization.Description))
                {
                    var customizationEffects = DetectActiveEffects(Array.Empty<string>(), selectedCustomization.Description, request, request.BossImmunityKeys, "Customization", weapon.Item.Name, weapon.Item.AbilityType, weapon.Item.Element);
                    detectedEffects.AddRange(customizationEffects);
                    var customizationHasThresholdSensitiveSetup = HasThresholdSensitiveSetupEffect(customizationEffects);
                    foreach (var effect in customizationEffects)
                    {
                        var effectScore = GetCustomizationEffectDelta(
                            effect,
                            weaponEffects,
                            weapon.Item.CommandAtb,
                            role,
                            request,
                            referenceTuningProfile,
                            slotName,
                            customizationHasThresholdSensitiveSetup,
                            actionHasThresholdSensitiveSetup,
                            out var rawEffectScore);
                        if (effectScore <= 0.001 && rawEffectScore <= 0.001)
                        {
                            continue;
                        }

                        providedKeys.Add(effect.Key);
                        rawActiveUtilityScore += effectScore;
                        atbAdjustment += effectScore - rawEffectScore;
                        nonPassiveScore += effectScore;
                        score += effectScore;
                        AccumulateActiveFamilyContribution(activeUtilityByFamily, effect, effectScore, role);
                        scoreBreakdown.Add(CreateScoreComponent("customization_effects", ToLabel(effect.Key), rawEffectScore));
                    }
                }

                if (Math.Abs(atbAdjustment) > 0.001)
                {
                    scoreBreakdown.Add(CreateScoreComponent("atb", $"ATB efficiency ({weapon.Item.CommandAtb} ATB)", atbAdjustment));
                }

                var initialChargeAdjustment = GetUltimateInitialChargeTimingAdjustment(weapon, slotName, damageScore, rawActiveUtilityScore);
                if (Math.Abs(initialChargeAdjustment) > 0.001)
                {
                    nonPassiveScore += initialChargeAdjustment;
                    score += initialChargeAdjustment;
                    scoreBreakdown.Add(CreateScoreComponent("timing", $"Initial charge timing ({weapon.Item.InitialChargeTimeSec}s)", initialChargeAdjustment));
                }
            }

            var battleFitMultiplier = GetWeaponBattleFitMultiplier(weapon, role, request, slotName, includeActiveEffects, includeDamage, passivePoints, providedKeys, detectedEffects, patk, matk, heal);
            if (battleFitMultiplier != 1.0)
            {
                var preFitScore = score;
                nonPassiveScore *= battleFitMultiplier;
                score *= battleFitMultiplier;
                foreach (var contribution in activeUtilityByFamily.Values)
                {
                    contribution.Score *= battleFitMultiplier;
                }

                scoreBreakdown.Add(CreateScoreComponent("fit", $"Battle fit multiplier ({battleFitMultiplier:0.##}x)", score - preFitScore));
            }

            ReconcileRoundedScoreBreakdown(scoreBreakdown, score);

            return new SlotEvaluation
            {
                Name = weapon.Item.Name,
                BattleFitMultiplier = battleFitMultiplier,
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
                    Range = weapon.Item.Range,
                    AbilityText = weapon.Snapshot.AbilityText,
                    CommandAtb = weapon.Item.CommandAtb,
                    InitialChargeTimeSec = weapon.Item.InitialChargeTimeSec,
                    UseCount = weapon.Item.UseCount,
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
                    ProvidedEffectLabels = providedKeys.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    ScoreBreakdown = CloneScoreBreakdown(scoreBreakdown)
                },
                PassivePoints = passivePoints,
                ProvidedEffectKeys = providedKeys.ToList(),
                NonPassiveScore = nonPassiveScore,
                ActiveUtilityByFamily = activeUtilityByFamily,
                ScoreBreakdown = CloneScoreBreakdown(scoreBreakdown),
                Score = score
            };
        }

        private SlotEvaluation CreateCostumeSlot(OwnedCostumeCandidate costume, CharacterRole role, PlayerPowerAnalyzerV2Request request, ReferenceTuningProfile referenceTuningProfile, string slotName, double slotMultiplier, bool includeActiveEffects)
        {
            var passivePoints = BuildPassivePointMap(costume.Item.MaxPassiveSkills, slotMultiplier, preferResolvedEffectLabels: true);
            var nonPassiveScore = 0d;
            var score = ScorePassivePoints(passivePoints, role, request);
            var scoreBreakdown = new List<PlayerPowerAnalyzerV2ScoreComponent>
            {
                CreateScoreComponent("passives", "Passive score", score)
            };
            var providedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var activeUtilityByFamily = new Dictionary<string, SlotActiveFamilyContribution>(StringComparer.OrdinalIgnoreCase);
            if (includeActiveEffects)
            {
                var atbAdjustment = 0d;
                var costumeEffects = DetectActiveEffects(costume.Item.EffectTags, costume.Item.AbilityText, request, request.BossImmunityKeys, slotName, costume.Item.Name, costume.Item.AbilityType, costume.Item.Element);
                var actionHasThresholdSensitiveSetup = HasThresholdSensitiveSetupEffect(costumeEffects);
                foreach (var effect in costumeEffects)
                {
                    providedKeys.Add(effect.Key);
                    var rawEffectScore = ScoreActiveEffectWithReferenceTuning(effect, role, request, referenceTuningProfile);
                    var effectScore = ApplyAtbEfficiencyToUtilityScore(rawEffectScore, costume.Item.CommandAtb, effect, role, slotName, actionHasThresholdSensitiveSetup);
                    atbAdjustment += effectScore - rawEffectScore;
                    nonPassiveScore += effectScore;
                    score += effectScore;
                    AccumulateActiveFamilyContribution(activeUtilityByFamily, effect, effectScore, role);
                    scoreBreakdown.Add(CreateScoreComponent("effects", ToLabel(effect.Key), rawEffectScore));
                }

                if (Math.Abs(atbAdjustment) > 0.001)
                {
                    scoreBreakdown.Add(CreateScoreComponent("atb", $"ATB efficiency ({costume.Item.CommandAtb} ATB)", atbAdjustment));
                }
            }

            var costumeFitMultiplier = GetCostumeBattleFitMultiplier(costume, role, request, slotName);
            if (costumeFitMultiplier != 1.0)
            {
                var preFitScore = score;
                nonPassiveScore *= costumeFitMultiplier;
                score *= costumeFitMultiplier;
                foreach (var contribution in activeUtilityByFamily.Values)
                {
                    contribution.Score *= costumeFitMultiplier;
                }

                scoreBreakdown.Add(CreateScoreComponent("fit", $"Battle fit multiplier ({costumeFitMultiplier:0.##}x)", score - preFitScore));
            }

            ReconcileRoundedScoreBreakdown(scoreBreakdown, score);

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
                    CommandAtb = costume.Item.CommandAtb,
                    InitialChargeTimeSec = costume.Item.InitialChargeTimeSec,
                    UseCount = costume.Item.UseCount,
                    SlotMultiplier = slotMultiplier,
                    Score = Math.Round(score, 2),
                    PassiveSummaries = passivePoints.OrderByDescending(kvp => kvp.Value).Take(5).Select(kvp => $"{FormatPassiveDisplayLabel(kvp.Key)} +{kvp.Value} pts").ToList(),
                    ProvidedEffectLabels = providedKeys.Select(ToLabel).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    ScoreBreakdown = CloneScoreBreakdown(scoreBreakdown)
                },
                PassivePoints = passivePoints,
                ProvidedEffectKeys = providedKeys.ToList(),
                NonPassiveScore = nonPassiveScore,
                ActiveUtilityByFamily = activeUtilityByFamily,
                ScoreBreakdown = CloneScoreBreakdown(scoreBreakdown),
                Score = score
            };
        }

        private static Dictionary<string, int> BuildPassivePointMap(IEnumerable<PassiveSkillTotal> passives, double slotMultiplier, bool preferResolvedEffectLabels = false)
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

                AddPassivePointValues(map, passive.SkillName, appliedPoints, passive.Effects, preferResolvedEffectLabels);
            }

            return map;
        }

        private static void AddPassivePointValues(
            Dictionary<string, int> destination,
            string skillName,
            int appliedPoints,
            IEnumerable<PassiveSkillEffectDetail>? passiveEffects = null,
            bool preferResolvedEffectLabels = false)
        {
            if (appliedPoints <= 0 || string.IsNullOrWhiteSpace(skillName))
            {
                return;
            }

            foreach (var scoringLabel in GetPassiveScoringLabels(skillName, passiveEffects, preferResolvedEffectLabels))
            {
                if (string.IsNullOrWhiteSpace(scoringLabel))
                {
                    continue;
                }

                destination[scoringLabel] = destination.TryGetValue(scoringLabel, out var existing)
                    ? existing + appliedPoints
                    : appliedPoints;
            }
        }

        private static IReadOnlyList<string> GetPassiveScoringLabels(string skillName, IEnumerable<PassiveSkillEffectDetail>? passiveEffects, bool preferResolvedEffectLabels)
        {
            var compositeLabels = SplitCompositePassiveSkillName(skillName);
            if (compositeLabels.Count > 1)
            {
                return compositeLabels;
            }

            var resolvedLabels = passiveEffects?
                .Select(effect => effect.Label)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            if (!preferResolvedEffectLabels
                || resolvedLabels.Count == 0
                || IsPassiveSkillNameDirectlyScorable(skillName))
            {
                return new[] { skillName };
            }

            return resolvedLabels.Count > 0
                ? resolvedLabels
                : new[] { skillName };
        }

        private static List<string> SplitCompositePassiveSkillName(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName) || !skillName.Contains('&', StringComparison.Ordinal))
            {
                return new List<string>();
            }

            return skillName
                .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => Regex.Replace(part, @"\s+", " ").Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsPassiveSkillNameDirectlyScorable(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return false;
            }

            var normalized = skillName.ToLowerInvariant();
            return normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost atk", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost pdef", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost mdef", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost hp", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("heal", StringComparison.OrdinalIgnoreCase)
                || IsElementAbilityPassiveLabel(normalized)
                || IsPhysAbilityPassiveLabel(normalized)
                || IsMagAbilityPassiveLabel(normalized)
                || IsGenericAbilityPassiveLabel(normalized);
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
                CharacterRole.Support => request.PreferredDamageType == DamageType.Magical
                    ? (patk * 0.03) + (matk * 0.05) + (heal * 0.05)
                    : (patk * 0.05) + (matk * 0.03) + (heal * 0.05),
                CharacterRole.Tank => (patk * 0.04) + (matk * 0.03) + (heal * 0.03),
                _ when request.PreferredDamageType == DamageType.Magical => (matk * 0.10) + (patk * 0.03) + (heal * 0.01),
                _ => (patk * 0.10) + (matk * 0.03) + (heal * 0.01)
            };
        }

        private static double ScorePassivePoints(Dictionary<string, int> passivePoints, CharacterRole role, PlayerPowerAnalyzerV2Request request, bool isOffHandSlotContext = false)
        {
            return passivePoints.Sum(kvp => ScorePassiveSkill(kvp.Key, kvp.Value, role, request, isOffHandSlotContext: isOffHandSlotContext));
        }

        private static double ScoreCharacterPassivePoints(
            IReadOnlyDictionary<string, int> passivePoints,
            IReadOnlyDictionary<string, int> teamWidePassivePoints,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request)
        {
            return ScoreCharacterPassivePoints(passivePoints, new[] { teamWidePassivePoints }, role, request);
        }

        private static double ScoreCharacterPassivePoints(
            IReadOnlyDictionary<string, int> passivePoints,
            IEnumerable<IReadOnlyDictionary<string, int>> teamWidePassiveProviderPoints,
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

            foreach (var providerPassivePoints in teamWidePassiveProviderPoints)
            {
                foreach (var kvp in providerPassivePoints)
                {
                    if (!IsTeamWidePassive(kvp.Key) || kvp.Value <= 0)
                    {
                        continue;
                    }

                    score += ScoreTeamWidePassiveSkillForRecipient(kvp.Key, kvp.Value, role, request);
                }
            }

            return score;
        }

        private static double GetSubWeaponMarginalGain(
            IReadOnlyDictionary<string, int> weaponPassivePoints,
            double nonPassiveScore,
            double battleFitMultiplier,
            IReadOnlyDictionary<string, int> currentCharacterPassivePoints,
            IReadOnlyDictionary<string, int> currentTeamWidePassivePoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<CharacterRole> teamRoles,
            PlayerPowerAnalyzerV2Request request)
        {
            return GetSubWeaponMarginalGain(
                weaponPassivePoints,
                nonPassiveScore,
                battleFitMultiplier,
                currentCharacterPassivePoints,
                currentTeamWidePassivePoints,
                equippingRole,
                teamRoles,
                Array.Empty<string>(),
                request);
        }

        private static double GetSubWeaponMarginalGain(
            IReadOnlyDictionary<string, int> weaponPassivePoints,
            double nonPassiveScore,
            double battleFitMultiplier,
            IReadOnlyDictionary<string, int> currentCharacterPassivePoints,
            IReadOnlyDictionary<string, int> currentTeamWidePassivePoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<CharacterRole> teamRoles,
            IReadOnlyCollection<string> equippingProvidedEffectKeys,
            PlayerPowerAnalyzerV2Request request)
        {
            return GetSubWeaponMarginalGain(
                weaponPassivePoints,
                nonPassiveScore,
                battleFitMultiplier,
                currentCharacterPassivePoints,
                currentTeamWidePassivePoints,
                equippingRole,
                teamRoles,
                equippingProvidedEffectKeys,
                true,
                request);
        }

        private static double GetSubWeaponMarginalGain(
            IReadOnlyDictionary<string, int> weaponPassivePoints,
            double nonPassiveScore,
            double battleFitMultiplier,
            IReadOnlyDictionary<string, int> currentCharacterPassivePoints,
            IReadOnlyDictionary<string, int> currentTeamWidePassivePoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<CharacterRole> teamRoles,
            IReadOnlyCollection<string> equippingProvidedEffectKeys,
            bool hasRequestedElementMainOrOffHand,
            PlayerPowerAnalyzerV2Request request)
        {
            return GetSubWeaponMarginalGainWithAnchorContext(
                weaponPassivePoints,
                nonPassiveScore,
                battleFitMultiplier,
                currentCharacterPassivePoints,
                currentTeamWidePassivePoints,
                equippingRole,
                teamRoles,
                equippingProvidedEffectKeys,
                string.Empty,
                null,
                CharacterRole.DPS,
                hasRequestedElementMainOrOffHand,
                request);
        }

        private static double GetSubWeaponMarginalGainWithAnchorContext(
            IReadOnlyDictionary<string, int> weaponPassivePoints,
            double nonPassiveScore,
            double battleFitMultiplier,
            IReadOnlyDictionary<string, int> currentCharacterPassivePoints,
            IReadOnlyDictionary<string, int> currentTeamWidePassivePoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<CharacterRole> teamRoles,
            string equippingCharacterName,
            string? anchorCharacterName,
            CharacterRole anchorRole,
            PlayerPowerAnalyzerV2Request request)
        {
            return GetSubWeaponMarginalGainWithAnchorContext(
                weaponPassivePoints,
                nonPassiveScore,
                battleFitMultiplier,
                currentCharacterPassivePoints,
                currentTeamWidePassivePoints,
                equippingRole,
                teamRoles,
                Array.Empty<string>(),
                equippingCharacterName,
                anchorCharacterName,
                anchorRole,
                request);
        }

        private static double GetSubWeaponMarginalGainWithAnchorContext(
            IReadOnlyDictionary<string, int> weaponPassivePoints,
            double nonPassiveScore,
            double battleFitMultiplier,
            IReadOnlyDictionary<string, int> currentCharacterPassivePoints,
            IReadOnlyDictionary<string, int> currentTeamWidePassivePoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<CharacterRole> teamRoles,
            IReadOnlyCollection<string> equippingProvidedEffectKeys,
            string equippingCharacterName,
            string? anchorCharacterName,
            CharacterRole anchorRole,
            PlayerPowerAnalyzerV2Request request)
        {
            return GetSubWeaponMarginalGainWithAnchorContext(
                weaponPassivePoints,
                nonPassiveScore,
                battleFitMultiplier,
                currentCharacterPassivePoints,
                currentTeamWidePassivePoints,
                equippingRole,
                teamRoles,
                equippingProvidedEffectKeys,
                equippingCharacterName,
                anchorCharacterName,
                anchorRole,
                true,
                request);
        }

        private static double GetSubWeaponMarginalGainWithAnchorContext(
            IReadOnlyDictionary<string, int> weaponPassivePoints,
            double nonPassiveScore,
            double battleFitMultiplier,
            IReadOnlyDictionary<string, int> currentCharacterPassivePoints,
            IReadOnlyDictionary<string, int> currentTeamWidePassivePoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<CharacterRole> teamRoles,
            IReadOnlyCollection<string> equippingProvidedEffectKeys,
            string equippingCharacterName,
            string? anchorCharacterName,
            CharacterRole anchorRole,
            bool hasRequestedElementMainOrOffHand,
            PlayerPowerAnalyzerV2Request request)
        {
            var effectiveBattleFitMultiplier = equippingRole == CharacterRole.DPS
                ? battleFitMultiplier
                : 1.0;
            var gain = nonPassiveScore * effectiveBattleFitMultiplier;
            var isAnchorEquipper = !string.IsNullOrWhiteSpace(anchorCharacterName)
                && equippingCharacterName.Equals(anchorCharacterName, StringComparison.OrdinalIgnoreCase);
            foreach (var kvp in weaponPassivePoints)
            {
                if (kvp.Value <= 0)
                {
                    continue;
                }

                if (IsTeamWidePassive(kvp.Key))
                {
                    var currentProviderPoints = currentCharacterPassivePoints.TryGetValue(kvp.Key, out var existingProviderPoints) ? existingProviderPoints : 0;
                    var nextProviderPoints = currentProviderPoints + kvp.Value;
                    var currentPoints = currentTeamWidePassivePoints.TryGetValue(kvp.Key, out var existingTeamWidePoints) ? existingTeamWidePoints : 0;
                    var nextPoints = currentPoints + kvp.Value;
                    var teamWidePassiveMarginalMultiplier = GetSubWeaponTeamWidePassiveMarginalMultiplier(kvp.Key, equippingRole, request);
                    foreach (var teamRole in teamRoles)
                    {
                        gain += (ScoreTeamWidePassiveSkillForRecipient(kvp.Key, nextPoints, teamRole, request)
                            - ScoreTeamWidePassiveSkillForRecipient(kvp.Key, currentPoints, teamRole, request))
                            * teamWidePassiveMarginalMultiplier;
                    }

                    if (!string.IsNullOrWhiteSpace(anchorCharacterName))
                    {
                        gain += GetAnchorTeamWidePassiveMarginalBonus(kvp.Key, currentPoints, nextPoints, anchorRole, request)
                            * teamWidePassiveMarginalMultiplier;
                    }

                    gain += GetDistributedProviderTeamWidePassiveMarginalBonus(
                        kvp.Key,
                        currentProviderPoints,
                        nextProviderPoints,
                        currentPoints,
                        equippingRole,
                        teamRoles,
                        request);

                    continue;
                }

                var currentCharacterPoints = currentCharacterPassivePoints.TryGetValue(kvp.Key, out var existingCharacterPoints) ? existingCharacterPoints : 0;
                var passiveDelta = ScorePassiveSkill(kvp.Key, currentCharacterPoints + kvp.Value, equippingRole, request, isSubWeaponContext: true)
                    - ScorePassiveSkill(kvp.Key, currentCharacterPoints, equippingRole, request, isSubWeaponContext: true);
                var selfPassiveCoherenceMultiplier = GetSubWeaponSelfPassiveCoherenceMultiplier(kvp.Key, equippingRole, hasRequestedElementMainOrOffHand, request);
                gain += passiveDelta * effectiveBattleFitMultiplier * selfPassiveCoherenceMultiplier;
                gain += GetSupportMaintenancePassiveMarginalBonus(
                    kvp.Key,
                    existingCharacterPoints,
                    currentCharacterPoints + kvp.Value,
                    equippingRole,
                    equippingProvidedEffectKeys,
                    request);

                if (isAnchorEquipper)
                {
                    gain += GetAnchorSelfPassiveMarginalBonus(kvp.Key, currentCharacterPoints, currentCharacterPoints + kvp.Value, anchorRole, request) * effectiveBattleFitMultiplier * selfPassiveCoherenceMultiplier;
                }
            }

            return gain;
        }

        private static double GetSubWeaponTeamWidePassiveMarginalMultiplier(string skillName, CharacterRole equippingRole, PlayerPowerAnalyzerV2Request request)
        {
            if (!ShouldFavorOffenseInDefaultSubWeaponAssignment(request) || string.IsNullOrWhiteSpace(skillName))
            {
                return 1.0;
            }

            var normalized = skillName.ToLowerInvariant();
            if (IsAnchorPriorityTeamWidePassive(skillName, request))
            {
                if (request.PreferredDamageType == DamageType.Physical
                    && normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase))
                {
                    return equippingRole == CharacterRole.DPS ? 0.94 : 1.0;
                }

                return equippingRole switch
                {
                    CharacterRole.Healer => 0.52,
                    CharacterRole.Support => 0.66,
                    CharacterRole.Tank => 0.8,
                    _ => 1.0
                };
            }

            if (!normalized.Contains("boost pdef (all allies)", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("boost mdef (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            return equippingRole switch
            {
                CharacterRole.Healer => 0.42,
                CharacterRole.Support => 0.5,
                CharacterRole.Tank => 0.78,
                CharacterRole.DPS => 0.62,
                _ => 1.0
            };
        }

        private static double GetDistributedProviderTeamWidePassiveMarginalBonus(
            string skillName,
            int currentProviderPoints,
            int nextProviderPoints,
            int currentTeamWidePoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<CharacterRole> teamRoles,
            PlayerPowerAnalyzerV2Request request)
        {
            if (!ShouldFavorOffenseInDefaultSubWeaponAssignment(request)
                || string.IsNullOrWhiteSpace(skillName)
                || currentTeamWidePoints <= 0
                || currentProviderPoints > 0
                || nextProviderPoints <= currentProviderPoints
                || !IsAnchorPriorityTeamWidePassive(skillName, request))
            {
                return 0d;
            }

            var providerDelta = 0d;
            foreach (var teamRole in teamRoles)
            {
                providerDelta += ScoreTeamWidePassiveSkillForRecipient(skillName, nextProviderPoints, teamRole, request)
                    - ScoreTeamWidePassiveSkillForRecipient(skillName, currentProviderPoints, teamRole, request);
            }

            var multiplier = equippingRole switch
            {
                CharacterRole.Healer => 0.46,
                CharacterRole.Support => 0.4,
                CharacterRole.Tank => 0.3,
                CharacterRole.DPS => 0.24,
                _ => 0.32
            };

            return providerDelta * multiplier;
        }

        private static double GetSubWeaponSelfPassiveCoherenceMultiplier(
            string skillName,
            CharacterRole equippingRole,
            bool hasRequestedElementMainOrOffHand,
            PlayerPowerAnalyzerV2Request request)
        {
            if (equippingRole != CharacterRole.DPS
                || hasRequestedElementMainOrOffHand
                || request.EnemyWeakness == Element.None
                || string.IsNullOrWhiteSpace(skillName))
            {
                return 1.0;
            }

            var normalized = skillName.ToLowerInvariant();
            if (IsElementOffensivePassiveLabel(normalized)
                && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return 0.32;
            }

            if (!ShouldFavorOffenseInDefaultSubWeaponAssignment(request))
            {
                return 1.0;
            }

            if (IsElementOffensivePassiveLabel(normalized))
            {
                return 0.5;
            }

            if (IsAnchorPrioritySelfPassive(skillName, request))
            {
                return 0.84;
            }

            return 1.0;
        }

        private static bool ShouldFavorOffenseInDefaultSubWeaponAssignment(PlayerPowerAnalyzerV2Request request)
        {
            if (request.PreferredDamageType == DamageType.Any)
            {
                return false;
            }

            return !request.HardRequiredEffectKeys.Concat(request.SoftPreferredEffectKeys)
                .Any(key => key.Equals("pdef_up", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("mdef_up", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasRequestedElementMainOrOffHand(CharacterBuildCandidate variant, PlayerPowerAnalyzerV2Request request)
        {
            if (request.EnemyWeakness == Element.None)
            {
                return true;
            }

            return new[] { variant.MainWeapon, variant.OffHandWeapon }
                .Where(slot => slot != null)
                .Any(slot => !string.IsNullOrWhiteSpace(slot!.Element)
                    && !slot.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                    && MatchesRequestedElement(slot.Element, request.EnemyWeakness));
        }

        private static double GetSupportMaintenancePassiveMarginalBonus(
            string skillName,
            int currentPoints,
            int nextPoints,
            CharacterRole equippingRole,
            IReadOnlyCollection<string> equippingProvidedEffectKeys,
            PlayerPowerAnalyzerV2Request request)
        {
            if (nextPoints <= currentPoints
                || !IsBuffDebuffExtensionPassive(skillName)
                || equippingRole == CharacterRole.DPS
                || equippingProvidedEffectKeys.Count == 0)
            {
                return 0d;
            }

            var extensionDelta = GetBuffDebuffExtensionBreakpointBonus(nextPoints)
                - GetBuffDebuffExtensionBreakpointBonus(currentPoints);
            if (extensionDelta <= 0.001)
            {
                return 0d;
            }

            var maintenanceCoverage = equippingProvidedEffectKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(effectKey => GetExtensionSupportedEffectMaintenanceWeight(skillName, effectKey, request))
                .Where(weight => weight > 0)
                .OrderByDescending(weight => weight)
                .Take(3)
                .Sum();
            if (maintenanceCoverage <= 0.001)
            {
                return 0d;
            }

            return extensionDelta
                * Math.Min(2.6, maintenanceCoverage)
                * GetMaintenancePassiveRoleMultiplier(equippingRole);
        }

        private static double GetOptimisticMaintenancePassiveUpperBound(
            string skillName,
            int points,
            CharacterRole equippingRole,
            PlayerPowerAnalyzerV2Request request)
        {
            if (points <= 0 || !IsBuffDebuffExtensionPassive(skillName))
            {
                return 0d;
            }

            var breakpointValue = GetBuffDebuffExtensionBreakpointBonus(points);
            var roleFactor = equippingRole switch
            {
                CharacterRole.Support => 2.6,
                CharacterRole.Healer => 2.3,
                CharacterRole.Tank => 1.9,
                _ => 0.2
            };
            return breakpointValue * roleFactor;
        }

        private static double GetAnchorSelfPassiveMarginalBonus(
            string skillName,
            int currentPoints,
            int nextPoints,
            CharacterRole anchorRole,
            PlayerPowerAnalyzerV2Request request)
        {
            if (nextPoints <= currentPoints || !IsAnchorPrioritySelfPassive(skillName, request))
            {
                return 0d;
            }

            var passiveDelta = ScorePassiveSkill(skillName, nextPoints, anchorRole, request)
                - ScorePassiveSkill(skillName, currentPoints, anchorRole, request);
            return passiveDelta * GetAnchorSelfPassivePriorityMultiplier(skillName, request);
        }

        private static double GetAnchorTeamWidePassiveMarginalBonus(
            string skillName,
            int currentPoints,
            int nextPoints,
            CharacterRole anchorRole,
            PlayerPowerAnalyzerV2Request request)
        {
            if (nextPoints <= currentPoints || !IsAnchorPriorityTeamWidePassive(skillName, request))
            {
                return 0d;
            }

            var passiveDelta = ScoreTeamWidePassiveSkillForRecipient(skillName, nextPoints, anchorRole, request)
                - ScoreTeamWidePassiveSkillForRecipient(skillName, currentPoints, anchorRole, request);
            return passiveDelta * GetAnchorTeamWidePassivePriorityMultiplier(skillName, request);
        }

        private static bool IsAnchorPrioritySelfPassive(string skillName, PlayerPowerAnalyzerV2Request request)
        {
            if (string.IsNullOrWhiteSpace(skillName) || IsTeamWidePassive(skillName))
            {
                return false;
            }

            var normalized = skillName.ToLowerInvariant();
            if (request.PreferredDamageType == DamageType.Magical)
            {
                if (normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase)
                    || IsMagAbilityPassiveLabel(normalized))
                {
                    return true;
                }
            }
            else if (request.PreferredDamageType == DamageType.Physical)
            {
                if (normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase)
                    || IsPhysAbilityPassiveLabel(normalized))
                {
                    return true;
                }
            }
            else if (normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase)
                || IsPhysAbilityPassiveLabel(normalized)
                || IsMagAbilityPassiveLabel(normalized))
            {
                return true;
            }

            if (normalized.Contains("boost atk", StringComparison.OrdinalIgnoreCase)
                || IsGenericAbilityPassiveLabel(normalized))
            {
                return true;
            }

            return request.EnemyWeakness != Element.None
                && IsElementAbilityPassiveLabel(normalized)
                && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAnchorPriorityTeamWidePassive(string skillName, PlayerPowerAnalyzerV2Request request)
        {
            if (string.IsNullOrWhiteSpace(skillName) || !IsTeamWidePassive(skillName))
            {
                return false;
            }

            var normalized = skillName.ToLowerInvariant();
            if (request.PreferredDamageType == DamageType.Magical)
            {
                return normalized.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)
                    || (IsElementAbilityAllAlliesPassiveLabel(normalized)
                        && request.EnemyWeakness != Element.None
                        && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
            }

            if (request.PreferredDamageType == DamageType.Physical)
            {
                return normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)
                    || (IsElementAbilityAllAlliesPassiveLabel(normalized)
                        && request.EnemyWeakness != Element.None
                        && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
            }

            return normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)
                || (IsElementAbilityAllAlliesPassiveLabel(normalized)
                    && request.EnemyWeakness != Element.None
                    && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        }

        private static double GetAnchorSelfPassivePriorityMultiplier(string skillName, PlayerPowerAnalyzerV2Request request)
        {
            var normalized = skillName.ToLowerInvariant();
            if (request.EnemyWeakness != Element.None
                && IsElementAbilityPassiveLabel(normalized)
                && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return 1.65;
            }

            if (request.PreferredDamageType == DamageType.Magical)
            {
                if (normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase)) return 1.55;
                if (IsMagAbilityPassiveLabel(normalized)) return 1.45;
            }
            else if (request.PreferredDamageType == DamageType.Physical)
            {
                if (normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase)) return 1.55;
                if (IsPhysAbilityPassiveLabel(normalized)) return 1.45;
            }
            else
            {
                if (normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase)) return 1.4;
                if (IsMagAbilityPassiveLabel(normalized) || IsPhysAbilityPassiveLabel(normalized)) return 1.3;
            }

            if (normalized.Contains("boost atk", StringComparison.OrdinalIgnoreCase)) return 1.15;
            if (IsGenericAbilityPassiveLabel(normalized)) return 1.1;
            return 0d;
        }

        private static double GetAnchorTeamWidePassivePriorityMultiplier(string skillName, PlayerPowerAnalyzerV2Request request)
        {
            var normalized = skillName.ToLowerInvariant();
            var beneficiaryMultiplier = GetProjectedTeamWideBeneficiaryMultiplier(normalized, request);
            if (request.EnemyWeakness != Element.None
                && IsElementAbilityAllAlliesPassiveLabel(normalized)
                && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return 0.08 * beneficiaryMultiplier;
            }

            if (request.PreferredDamageType == DamageType.Magical
                && normalized.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return 0.06 * beneficiaryMultiplier;
            }

            if (request.PreferredDamageType == DamageType.Physical
                && normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return (ShouldFavorOffenseInDefaultSubWeaponAssignment(request) ? 0.18 : 0.06) * beneficiaryMultiplier;
            }

            return normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)
                ? 0.04 * beneficiaryMultiplier
                : 0d;
        }

        private static PlayerPowerAnalyzerV2ScoreComponent CreateScoreComponent(string category, string label, double value)
        {
            return new PlayerPowerAnalyzerV2ScoreComponent
            {
                Category = category,
                Label = label,
                Value = Math.Round(value, 2)
            };
        }

        private static void ReconcileRoundedScoreBreakdown(List<PlayerPowerAnalyzerV2ScoreComponent> breakdown, double expectedScore)
        {
            var roundedExpectedScore = Math.Round(expectedScore, 2);
            var roundedActualScore = Math.Round(breakdown.Sum(component => component.Value), 2);
            var delta = Math.Round(roundedExpectedScore - roundedActualScore, 2);
            if (Math.Abs(delta) >= 0.01)
            {
                breakdown.Add(CreateScoreComponent("rounding", "Rounding reconciliation", delta));
            }
        }

        private static List<PlayerPowerAnalyzerV2ScoreComponent> CloneScoreBreakdown(IEnumerable<PlayerPowerAnalyzerV2ScoreComponent> breakdown)
        {
            return breakdown
                .Select(component => new PlayerPowerAnalyzerV2ScoreComponent
                {
                    Category = component.Category,
                    Label = component.Label,
                    Value = component.Value
                })
                .ToList();
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

        private static List<string> BuildCharacterKeyRAbilities(
            string characterName,
            IReadOnlyDictionary<string, Dictionary<string, int>> passivePointsByCharacter)
        {
            var keyRAbilities = new List<(string Label, int Points, string SortKey)>();

            if (passivePointsByCharacter.TryGetValue(characterName, out var characterPassivePoints))
            {
                keyRAbilities.AddRange(characterPassivePoints
                    .Where(kvp => kvp.Value > 0 && !IsTeamWidePassive(kvp.Key))
                    .Select(kvp => (Label: $"{FormatPassiveDisplayLabel(kvp.Key)} +{kvp.Value} pts", Points: kvp.Value, SortKey: kvp.Key)));
            }

            foreach (var provider in passivePointsByCharacter.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                keyRAbilities.AddRange(provider.Value
                    .Where(kvp => kvp.Value > 0 && IsTeamWidePassive(kvp.Key))
                    .Select(kvp => (Label: $"{FormatPassiveDisplayLabel(kvp.Key)} [{provider.Key}] +{kvp.Value} pts", Points: kvp.Value, SortKey: $"{kvp.Key} [{provider.Key}]")));
            }

            return keyRAbilities
                .OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Label)
                .ToList();
        }

        private static string FormatPassiveDisplayLabel(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return skillName;
            }

            var normalized = skillName.ToLowerInvariant();
            if (normalized.Contains("reprieve", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("fatal damage", StringComparison.OrdinalIgnoreCase)
                || ((normalized.Contains("survive", StringComparison.OrdinalIgnoreCase)
                        || normalized.Contains("remain", StringComparison.OrdinalIgnoreCase)
                        || normalized.Contains("left", StringComparison.OrdinalIgnoreCase))
                    && normalized.Contains("1 hp", StringComparison.OrdinalIgnoreCase)))
            {
                return "Reprieve";
            }

            return skillName;
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

            if (IsElementAbilityAllAlliesPassiveLabel(normalized)
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
            var baseWeight = GetProjectedTeamWideBaseWeight(normalized, request);
            return baseWeight * GetProjectedTeamWideBeneficiaryMultiplier(normalized, request);
        }

        private static double GetProjectedTeamWideBaseWeight(string normalizedSkillName, PlayerPowerAnalyzerV2Request request)
        {
            if (normalizedSkillName.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 0.45 : 3.15;
            if (normalizedSkillName.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 3.15 : 0.45;
            if (normalizedSkillName.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)) return 2.55;
            if (IsGenericElementPotArcanumAllAlliesPassiveLabel(normalizedSkillName)) return request.EnemyWeakness != Element.None ? 3.3 : 2.75;
            if (IsElementAbilityAllAlliesPassiveLabel(normalizedSkillName) && request.EnemyWeakness != Element.None && normalizedSkillName.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) return 3.05;
            if (IsElementAbilityAllAlliesPassiveLabel(normalizedSkillName) && ContainsElementName(normalizedSkillName)) return 0.24;
            if (normalizedSkillName.Contains("boost pdef (all allies)", StringComparison.OrdinalIgnoreCase) || normalizedSkillName.Contains("boost mdef (all allies)", StringComparison.OrdinalIgnoreCase)) return 2.0;
            if (normalizedSkillName.Contains("all allies", StringComparison.OrdinalIgnoreCase)) return 2.4;
            return 0.55;
        }

        private static double GetProjectedTeamWideBeneficiaryMultiplier(string normalizedSkillName, PlayerPowerAnalyzerV2Request request)
        {
            if (request.EnemyWeakness != Element.None
                && IsElementAbilityAllAlliesPassiveLabel(normalizedSkillName)
                && normalizedSkillName.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return 1.2;
            }

            if (IsGenericElementPotArcanumAllAlliesPassiveLabel(normalizedSkillName))
            {
                return request.EnemyWeakness != Element.None ? 1.22 : 1.08;
            }

            if (request.PreferredDamageType == DamageType.Physical)
            {
                if (normalizedSkillName.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)) return 1.18;
                if (normalizedSkillName.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)) return 1.12;
            }
            else if (request.PreferredDamageType == DamageType.Magical)
            {
                if (normalizedSkillName.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase)) return 1.18;
                if (normalizedSkillName.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)) return 1.12;
            }
            else if (normalizedSkillName.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)
                || normalizedSkillName.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)
                || normalizedSkillName.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase))
            {
                return 1.08;
            }

            return 1.0;
        }

        private static double ScorePassiveSkill(string skillName, int points, CharacterRole role, PlayerPowerAnalyzerV2Request request, bool isSubWeaponContext = false, bool isOffHandSlotContext = false)
        {
            if (points <= 0)
            {
                return 0;
            }

            var scaledValue = TryGetPassiveBonusValue(skillName, points, out var bonusValue)
                ? bonusValue
                : points;

            // Value the passive by its real bonus % (scaledValue) converted to effective damage, rather
            // than the legacy abstract weight. Off-hand/sub halving is already reflected in `points` via
            // the slot multiplier, so the slot-context flags are no longer needed here.
            return scaledValue * GetPassiveEffectiveDamageFactor(skillName, role, request);
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

            if (IsGenericElementPotArcanumAllAlliesPassiveLabel(normalized))
            {
                bonusValue = ResolveBreakpointBonus(points, ElementPotArcanumAllAlliesBreakpointPoints, ElementPotArcanumAllAlliesBonuses);
                return true;
            }

            if (IsElementAbilityAllAlliesPassiveLabel(normalized))
            {
                bonusValue = ResolveBreakpointBonus(points, ElementAbilityAllAlliesBreakpointPoints, ElementAbilityAllAlliesBonuses);
                return true;
            }

            if (IsBuffDebuffExtensionPassive(normalized))
            {
                bonusValue = GetBuffDebuffExtensionBreakpointBonus(points);
                return true;
            }

            if (normalized.Contains("boost heal", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, BoostHealBreakpointPoints, BoostHealBonuses);
                return true;
            }

            if (normalized.Contains("Boost Phys. Ability Pot", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Boost Mag. Ability Pot", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Phys. Ability Dmg", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Phys. Ability Damage", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Mag. Ability Dmg", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Mag. Ability Damage", StringComparison.OrdinalIgnoreCase))
            {
                bonusValue = ResolveBreakpointBonus(points, StandardBreakpointPoints, BoostPhysAndMagAbilityPotBonuses);
                return true;
            }

            if (normalized.Contains("Boost Ability Pot", StringComparison.OrdinalIgnoreCase)
                || ((normalized.Contains("Ability Dmg", StringComparison.OrdinalIgnoreCase)
                        || normalized.Contains("Ability Damage", StringComparison.OrdinalIgnoreCase))
                    && !ContainsElementName(normalized)
                    && !normalized.Contains("Phys.", StringComparison.OrdinalIgnoreCase)
                    && !normalized.Contains("Mag.", StringComparison.OrdinalIgnoreCase)))
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

            if (IsElementAbilityPassiveLabel(normalized))
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

        private static bool IsElementResistPassiveLabel(string normalized)
        {
            return ContainsElementName(normalized)
                && (normalized.Contains("resist", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("resistance", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsElementOffensivePassiveLabel(string normalized)
        {
            return ContainsElementName(normalized)
                && !IsElementResistPassiveLabel(normalized)
                && (normalized.Contains("pot.", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("potency", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("ability", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("mastery", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("arcanum", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsElementAbilityAllAlliesPassiveLabel(string normalized)
        {
            return normalized.Contains("all allies", StringComparison.OrdinalIgnoreCase)
                && (IsElementAbilityPassiveLabel(normalized) || IsGenericElementPotArcanumAllAlliesPassiveLabel(normalized));
        }

        private static bool IsGenericElementPotArcanumAllAlliesPassiveLabel(string normalized)
        {
            return normalized.Contains("all allies", StringComparison.OrdinalIgnoreCase)
                && (normalized.Contains("boost elem. pot. arcanum", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("elem. pot. arcanum", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("element pot arcanum", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("elemental potency arcanum", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsElementAbilityPassiveLabel(string normalized)
        {
            return ContainsElementName(normalized)
                && (normalized.Contains("ability (all allies)", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("ability dmg", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("ability damage", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("ability pot", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("mastery", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("pot.", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPhysAbilityPassiveLabel(string normalized)
        {
            return normalized.Contains("phys. ability pot", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("phys. ability dmg", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("phys. ability damage", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMagAbilityPassiveLabel(string normalized)
        {
            return normalized.Contains("mag. ability pot", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("mag. ability dmg", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("mag. ability damage", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGenericAbilityPassiveLabel(string normalized)
        {
            return normalized.Contains("boost ability pot", StringComparison.OrdinalIgnoreCase)
                || ((normalized.Contains("ability pot", StringComparison.OrdinalIgnoreCase)
                        || normalized.Contains("ability dmg", StringComparison.OrdinalIgnoreCase)
                        || normalized.Contains("ability damage", StringComparison.OrdinalIgnoreCase))
                    && !ContainsElementName(normalized)
                    && !normalized.Contains("phys.", StringComparison.OrdinalIgnoreCase)
                    && !normalized.Contains("mag.", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBuffDebuffExtensionPassive(string skillName)
        {
            return AppliesToBuffExtensionPassive(skillName)
                || AppliesToDebuffExtensionPassive(skillName);
        }

        private static double GetBuffDebuffExtensionBreakpointBonus(int points)
        {
            return ResolveBreakpointBonus(points, StandardBreakpointPoints, BuffDebuffExtensionBonuses);
        }

        private static bool AppliesToBuffExtensionPassive(string skillName)
        {
            return !string.IsNullOrWhiteSpace(skillName)
                && (skillName.Contains("Buff/Debuff Extension", StringComparison.OrdinalIgnoreCase)
                    || skillName.Contains("Buff Extension", StringComparison.OrdinalIgnoreCase));
        }

        private static bool AppliesToDebuffExtensionPassive(string skillName)
        {
            return !string.IsNullOrWhiteSpace(skillName)
                && (skillName.Contains("Buff/Debuff Extension", StringComparison.OrdinalIgnoreCase)
                    || skillName.Contains("Debuff Extension", StringComparison.OrdinalIgnoreCase));
        }

        private static double GetExtensionSupportedEffectMaintenanceWeight(string skillName, string effectKey, PlayerPowerAnalyzerV2Request request)
        {
            if (string.IsNullOrWhiteSpace(effectKey))
            {
                return 0d;
            }

            if (IsExtendableBuffEffectKey(effectKey))
            {
                if (!AppliesToBuffExtensionPassive(skillName))
                {
                    return 0d;
                }

                return effectKey switch
                {
                    "patk_up" => request.PreferredDamageType == DamageType.Physical ? 1.0 : 0.4,
                    "matk_up" => request.PreferredDamageType == DamageType.Magical ? 1.0 : 0.4,
                    "pdef_up" or "mdef_up" => 0.45,
                    "elemental_damage_up" => request.EnemyWeakness == Element.None ? 0.85 : 1.15,
                    _ => 0.35
                };
            }

            if (IsExtendableDebuffEffectKey(effectKey))
            {
                if (!AppliesToDebuffExtensionPassive(skillName))
                {
                    return 0d;
                }

                return effectKey switch
                {
                    "pdef_down" => request.PreferredDamageType == DamageType.Physical ? 1.2 : 0.45,
                    "mdef_down" => request.PreferredDamageType == DamageType.Magical ? 1.2 : 0.45,
                    "elemental_resistance_down" => request.EnemyWeakness == Element.None ? 1.05 : 1.35,
                    "patk_down" or "matk_down" => 0.55,
                    _ => 0.35
                };
            }

            return 0d;
        }

        private static double GetMaintenancePassiveRoleMultiplier(CharacterRole role)
        {
            return role switch
            {
                CharacterRole.Support => 1.0,
                CharacterRole.Healer => 0.92,
                CharacterRole.Tank => 0.8,
                _ => 0.18
            };
        }

        private static bool IsDefensiveOrHealingContextRequested(PlayerPowerAnalyzerV2Request request)
        {
            var defensiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pdef_up", "mdef_up", "healing_support" };
            return request.HardRequiredEffectKeys.Any(k => defensiveKeys.Contains(k))
                || request.SoftPreferredEffectKeys.Any(k => defensiveKeys.Contains(k));
        }

        // Sigil/stream/interrupt-phase R-abilities: only active during a battle's sigil phase (if it has one),
        // so they carry no persistent value in a normal build. Covers Interruption and the sigil-break boosts
        // (Triangle/Cross/Circle/Square "(Sigil) Boost"). Matches "sigil"/"interrupt" plus the bare shape
        // boosts (Triangle Boost has no "sigil" in its label). Avoids "Spade Customization" (a weapon unlock).
        private static bool IsSituationalSigilPhasePassive(string normalized)
        {
            return normalized.Contains("interrupt", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("sigil", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("triangle boost", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("circle boost", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("cross boost", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("square boost", StringComparison.OrdinalIgnoreCase);
        }

        // Passive R-abilities that contribute survivability/sustain rather than offense: defenses, elemental
        // resistances (resistance UP, not the offensive resistance-down debuff), and healing/cleanse spells.
        private static bool IsDefensiveOrSustainPassiveLabel(string normalized)
        {
            if (normalized.Contains("heal", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("cure", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("esuna", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("regen", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("medica", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("raise", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.Contains("pdef", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("mdef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return (normalized.Contains("resist", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("resistance", StringComparison.OrdinalIgnoreCase))
                && !normalized.Contains("down", StringComparison.OrdinalIgnoreCase);
        }

        // How much a SELF offensive R-ability matters: only to the extent this character itself deals
        // damage. A DPS gets full value; a Support/Healer/Tank's own stat boost barely moves team damage.
        private static double GetSelfPassiveBeneficiaryFactor(CharacterRole role)
        {
            return role switch
            {
                CharacterRole.DPS => 1.0,
                CharacterRole.Support => 0.25,
                CharacterRole.Tank => 0.2,
                CharacterRole.Healer => 0.15,
                _ => 0.5
            };
        }

        // Effective-damage conversion for an R-ability (passive): how much 1 percentage-point of the
        // passive's real bonus (from TryGetPassiveBonusValue's breakpoint tables) translates into effective
        // damage for this character/role/build. Replaces the abstract GetPassiveWeight multiplier with a
        // value grounded in real percentages, so e.g. Boost Ability Pot L5->L6 (+5%) is correctly worth
        // less than a first MATK (All Allies) source (+~14%). Per-target basis: All-Allies team-size
        // scaling is layered on separately at team scoring; off-axis / defensive passives go to ~0 in
        // offensive builds.
        private static double GetPassiveEffectiveDamageFactor(string skillName, CharacterRole role, PlayerPowerAnalyzerV2Request request)
        {
            var normalized = skillName.ToLowerInvariant();
            var isAllAllies = normalized.Contains("all allies", StringComparison.OrdinalIgnoreCase);

            // Sigil/stream/interrupt-phase R-abilities only matter during a battle's sigil phase, which not
            // every battle has (and then only for the sigil shape that fight requires). This covers
            // Interruption (e.g. "Ultimate Magic Interruption (All Allies)") AND the sigil-break boosts
            // (e.g. "△ Triangle Boost", "✕ Cross Sigil Boost", "◯ Circle Sigil Boost" — the (+N) is extra
            // sigils destroyed, not a persistent stat). In a normal build they earn no standing value. (A
            // future "sigil/interrupt phase" request flag could re-enable them; until then they are gated.)
            if (IsSituationalSigilPhasePassive(normalized))
            {
                return 0.05;
            }

            // Buff/Debuff Extension only helps a character that actually casts extendable buffs/debuffs
            // (fewer recasts to sustain them). Per-character buff output isn't known here, so use a modest
            // base; this is refined where buff output is known.
            if (IsBuffDebuffExtensionPassive(normalized))
            {
                return 0.3;
            }

            // Defensive / sustain passives contribute no offensive damage; near-zero unless defense asked.
            if (IsDefensiveOrSustainPassiveLabel(normalized))
            {
                return IsDefensiveOrHealingContextRequested(request) ? 0.6 : 0.05;
            }

            var physical = request.PreferredDamageType == DamageType.Physical;
            var magical = request.PreferredDamageType == DamageType.Magical;
            var anyType = request.PreferredDamageType == DamageType.Any;

            // Element-offensive potency/ability/arcanum passives: on-axis only when the element matches the
            // target's weakness (or it is a generic element-pot passive that adapts to whatever you run).
            if (IsElementOffensivePassiveLabel(normalized) || IsGenericElementPotArcanumAllAlliesPassiveLabel(normalized))
            {
                var onElement = IsGenericElementPotArcanumAllAlliesPassiveLabel(normalized)
                    || (request.EnemyWeakness != Element.None
                        && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
                if (!onElement)
                {
                    return 0.05;
                }

                return isAllAllies ? AllAlliesOffensiveBeneficiaryProxy : GetSelfPassiveBeneficiaryFactor(role);
            }

            // Stat / ability-potency boosts: only count toward the requested damage type.
            bool onAxis;
            if (normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase) || IsMagAbilityPassiveLabel(normalized))
            {
                onAxis = magical || anyType;
            }
            else if (normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase) || IsPhysAbilityPassiveLabel(normalized))
            {
                onAxis = physical || anyType;
            }
            else if (normalized.Contains("boost atk", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("boost ability pot", StringComparison.OrdinalIgnoreCase)
                || IsGenericAbilityPassiveLabel(normalized))
            {
                onAxis = true; // ATK (both types) and generic ability potency help either damage type
            }
            else
            {
                return 0.2; // unrecognized passive — small neutral contribution
            }

            if (!onAxis)
            {
                return 0.05;
            }

            // On-axis: per-target effective damage ≈ the raw %. An All-Allies passive feeds every offensive
            // ally, so it is worth more than a single character's self boost (proxy for the offensive core;
            // refined by actual team composition at team scoring). A self boost helps only this character.
            return isAllAllies ? AllAlliesOffensiveBeneficiaryProxy : GetSelfPassiveBeneficiaryFactor(role);
        }

        private static double GetPassiveWeight(string skillName, CharacterRole role, PlayerPowerAnalyzerV2Request request, bool isSubWeaponContext = false, bool isOffHandSlotContext = false)
        {
            var normalized = skillName.ToLowerInvariant();
            if (normalized.Contains("boost patk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 1.6 : 3.0;
            if (normalized.Contains("boost matk (all allies)", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 3.0 : 1.6;
            if (normalized.Contains("boost atk (all allies)", StringComparison.OrdinalIgnoreCase)) return 2.5;
            if (IsGenericElementPotArcanumAllAlliesPassiveLabel(normalized)) return request.EnemyWeakness != Element.None ? 3.25 : 2.7;
            if (IsElementAbilityAllAlliesPassiveLabel(normalized) && request.EnemyWeakness != Element.None && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) return 3.0;
            if (IsElementAbilityAllAlliesPassiveLabel(normalized) && ContainsElementName(normalized)) return 0.22;
            if (normalized.Contains("boost pdef (all allies)", StringComparison.OrdinalIgnoreCase) || normalized.Contains("boost mdef (all allies)", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.DPS ? 1.2 : 2.2;
            if (normalized.Contains("all allies", StringComparison.OrdinalIgnoreCase)) return 2.9;
            if (role != CharacterRole.DPS && TryGetNonDpsSelfOnlyOffensivePassiveWeight(normalized, request, out var nonDpsOffensiveWeight)) return nonDpsOffensiveWeight;
            if (IsBuffDebuffExtensionPassive(normalized)) return role switch { CharacterRole.Support => 0.18, CharacterRole.Healer => 0.16, CharacterRole.Tank => 0.14, _ => 0.02 };
            if (normalized.Contains("boost patk", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 0.8 : 2.0;
            if (normalized.Contains("boost matk", StringComparison.OrdinalIgnoreCase)) return request.PreferredDamageType == DamageType.Magical ? 2.0 : 0.8;
            if (normalized.Contains("boost atk", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.Healer ? 0.9 : 1.35;
            if (IsPhysAbilityPassiveLabel(normalized)) return request.PreferredDamageType == DamageType.Magical ? 0.8 : 2.3;
            if (IsMagAbilityPassiveLabel(normalized)) return request.PreferredDamageType == DamageType.Magical ? 2.3 : 0.8;
            if (IsGenericAbilityPassiveLabel(normalized)) return 1.85;
            if (request.EnemyWeakness != Element.None
                && IsElementOffensivePassiveLabel(normalized)
                && normalized.Contains(request.EnemyWeakness.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) return 2.4;
            if (IsElementAbilityPassiveLabel(normalized) && ContainsElementName(normalized)) return 0.12;
            if (normalized.Contains("boost hp", StringComparison.OrdinalIgnoreCase)) return role == CharacterRole.Tank ? 1.6 : 1.0;
            if (normalized.Contains("heal", StringComparison.OrdinalIgnoreCase))
            {
                if (role != CharacterRole.Healer) return 0.9;
                if (IsDefensiveOrHealingContextRequested(request)) return 2.0;
                return isSubWeaponContext ? 0.35 : (isOffHandSlotContext ? 1.5 : 1.2);
            }
            if (normalized.Contains("pdef", StringComparison.OrdinalIgnoreCase) || normalized.Contains("mdef", StringComparison.OrdinalIgnoreCase))
            {
                return role == CharacterRole.DPS ? 0.6 : 1.2;
            }
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

            if (IsPhysAbilityPassiveLabel(normalizedSkillName))
            {
                weight = request.PreferredDamageType == DamageType.Magical ? 0.1 : 0.24;
                return true;
            }

            if (IsMagAbilityPassiveLabel(normalizedSkillName))
            {
                weight = request.PreferredDamageType == DamageType.Magical ? 0.24 : 0.1;
                return true;
            }

            if (IsGenericAbilityPassiveLabel(normalizedSkillName))
            {
                weight = 0.22;
                return true;
            }

            if (IsElementAbilityPassiveLabel(normalizedSkillName) && ContainsElementName(normalizedSkillName))
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
            var typeMultiplier = request.PreferredDamageType == DamageType.Any || MatchesRequestedDamageType(weapon.Item.AbilityType, request.PreferredDamageType) ? 1.0 : 0.65;
            var elementMultiplier = request.EnemyWeakness == Element.None || MatchesRequestedElement(weapon.Item.Element, request.EnemyWeakness) || weapon.Item.Element.Equals("None", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.55;
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
                "phys_damage_received_up" => request.PreferredDamageType == DamageType.Magical ? 55 : 175,
                "mag_damage_received_up" => request.PreferredDamageType == DamageType.Magical ? 175 : 55,
                "phys_damage_bonus" or "phys_weapon_boost" => request.PreferredDamageType == DamageType.Magical ? 45 : 145,
                "mag_damage_bonus" or "mag_weapon_boost" => request.PreferredDamageType == DamageType.Magical ? 145 : 45,
                // Ability amplification — a strong multiplier (~+40%) but count-limited (see the count
                // discount in ScoreActiveEffectWithReferenceTuning). On-axis only for the requested type.
                "phys_ability_amplification" => request.PreferredDamageType == DamageType.Magical ? 45 : 150,
                "mag_ability_amplification" => request.PreferredDamageType == DamageType.Magical ? 150 : 45,
                "elemental_ability_amplification" => 150,
                "stat_debuff_tier_increase" => role == CharacterRole.DPS ? 120 : 170,
                "stat_buff_tier_increase" => role == CharacterRole.DPS ? 145 : 190,
                "pdef_down" => request.PreferredDamageType == DamageType.Magical ? 108 : 170,
                "mdef_down" => request.PreferredDamageType == DamageType.Magical ? 170 : 108,
                "patk_down" => request.PreferredDamageType == DamageType.Magical ? 100 : 120,
                "matk_down" => request.PreferredDamageType == DamageType.Magical ? 120 : 100,
                "pdef_up" or "mdef_up" => role == CharacterRole.DPS ? 75 : 110,
                "patk_up" => request.PreferredDamageType == DamageType.Magical ? 90 : 125,
                "matk_up" => request.PreferredDamageType == DamageType.Magical ? 125 : 90,
                "haste" => 115,
                "atb_conservation" => role == CharacterRole.DPS ? 88 : 118,
                "atb_gain" => role == CharacterRole.DPS ? 92 : 122,
                "exploit_weakness" => 148,
                "enfeeble" => 135,
                "enliven" => 95,
                "torpor" => 120,
                "gear_c_ability_uses" => 0,
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
            var score = providedEffectKeys.Sum(key => ScoreEffectKey(key, CharacterRole.Support, request, false));
            if (providedEffectKeys.Contains("stat_buff_tier_increase", StringComparer.OrdinalIgnoreCase)
                && providedEffectKeys.Any(key => key is "patk_up" or "matk_up" or "pdef_up" or "mdef_up"))
            {
                score += request.PreferredDamageType == DamageType.Magical ? 58 : 64;
            }

            if ((providedEffectKeys.Contains("atb_conservation", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("atb_gain", StringComparer.OrdinalIgnoreCase))
                && providedEffectKeys.Any(key => key is "patk_up" or "matk_up" or "elemental_damage_up" or "elemental_damage_bonus" or "elemental_weapon_boost" or "phys_damage_bonus" or "phys_weapon_boost" or "mag_damage_bonus" or "mag_weapon_boost" or "exploit_weakness"))
            {
                score += providedEffectKeys.Contains("atb_gain", StringComparer.OrdinalIgnoreCase) ? 40 : 34;
            }

            return score;
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

        private static double GetActiveEffectTargetingMultiplier(string key, CharacterRole role, PlayerPowerAnalyzerV2Request request, string sourceText, string? sourceAbilityType = null, string? sourceElement = null)
        {
            var isSelfOnly = IsSelfOnlyTargeting(sourceText);
            if (isSelfOnly && IsOffensiveSetupEffect(key))
            {
                if (role != CharacterRole.DPS)
                {
                    return 0.2;
                }

                var mismatchedType = request.PreferredDamageType != DamageType.Any
                    && !string.IsNullOrWhiteSpace(sourceAbilityType)
                    && !MatchesRequestedDamageType(sourceAbilityType, request.PreferredDamageType);
                var mismatchedElement = request.EnemyWeakness != Element.None
                    && !string.IsNullOrWhiteSpace(sourceElement)
                    && !sourceElement.Equals("None", StringComparison.OrdinalIgnoreCase)
                    && !MatchesRequestedElement(sourceElement, request.EnemyWeakness);
                if (mismatchedType || mismatchedElement)
                {
                    return 0.35;
                }
            }

            if (IsAllAlliesTargeting(sourceText))
            {
                return key switch
                {
                    "patk_up" => request.PreferredDamageType == DamageType.Magical ? 1.0 : 1.45,
                    "matk_up" => request.PreferredDamageType == DamageType.Magical ? 1.45 : 1.0,
                    "pdef_up" or "mdef_up" => role == CharacterRole.DPS ? 1.0 : 1.2,
                    "atb_conservation" => role == CharacterRole.DPS ? 1.14 : 1.24,
                    "atb_gain" => role == CharacterRole.DPS ? 1.18 : 1.28,
                    "healing_support" => role == CharacterRole.Healer ? 1.18 : 1.08,
                    _ => 1.1
                };
            }

            if (IsOtherAlliesTargeting(sourceText))
            {
                return key switch
                {
                    "patk_up" => request.PreferredDamageType == DamageType.Magical ? 0.95 : 1.22,
                    "matk_up" => request.PreferredDamageType == DamageType.Magical ? 1.22 : 0.95,
                    "atb_conservation" => 1.12,
                    "atb_gain" => 1.14,
                    _ => 1.05
                };
            }

            if (IsSingleAllyTargeting(sourceText))
            {
                return key switch
                {
                    "patk_up" => request.PreferredDamageType == DamageType.Magical ? 0.85 : 1.08,
                    "matk_up" => request.PreferredDamageType == DamageType.Magical ? 1.08 : 0.85,
                    "atb_conservation" => 1.04,
                    "atb_gain" => 1.08,
                    _ => 0.96
                };
            }

            return 1.0;
        }

        private static bool IsOffensiveSetupEffect(string key)
        {
            return key is "patk_up"
                or "matk_up"
                or "elemental_damage_up"
                or "elemental_damage_bonus"
                or "elemental_weapon_boost"
                or "elemental_damage_received_up"
                or "phys_damage_bonus"
                or "mag_damage_bonus"
                or "phys_weapon_boost"
                or "mag_weapon_boost"
                or "phys_damage_received_up"
                or "mag_damage_received_up"
                or "exploit_weakness";
        }

        private static bool HasThresholdSensitiveSetupEffect(IEnumerable<DetectedActiveEffect> effects)
        {
            return effects.Any(effect => IsThresholdSensitiveSetupUtility(effect.Key));
        }

        private static double ApplyAtbEfficiencyToUtilityScore(double baseScore, int commandAtb, DetectedActiveEffect effect, CharacterRole role, string slotName, bool actionHasThresholdSensitiveSetup)
        {
            if (baseScore == 0
                || commandAtb <= 0
                || !ShouldApplyAtbEfficiencyAdjustment(effect, role, slotName, actionHasThresholdSensitiveSetup))
            {
                return baseScore;
            }

            return baseScore * GetAtbEfficiencyMultiplier(commandAtb, role);
        }

        private static bool ShouldApplyAtbEfficiencyAdjustment(DetectedActiveEffect effect, CharacterRole role, string slotName, bool actionHasThresholdSensitiveSetup)
        {
            if (slotName.Equals("Sub Weapon", StringComparison.OrdinalIgnoreCase)
                || slotName.Equals("Sub Outfit", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (role == CharacterRole.DPS && effect.TargetScope == ActiveEffectTargetScope.Self)
            {
                return false;
            }

            return IsThresholdSensitiveSetupUtility(effect.Key)
                || effect.Key is "pdef_down"
                    or "mdef_down"
                    or "elemental_resistance_down"
                    or "stat_buff_tier_increase"
                    or "stat_debuff_tier_increase"
                    or "atb_conservation"
                    or "atb_gain"
                    or "enfeeble"
                    or "enliven"
                    or "torpor"
                || (actionHasThresholdSensitiveSetup
                    && role is CharacterRole.Healer or CharacterRole.Support
                    && effect.Key == "healing_support");
        }

        private static bool IsThresholdSensitiveSetupUtility(string key)
        {
            return IsOffensiveSetupEffect(key)
                || key is "pdef_up"
                    or "mdef_up"
                || key is "pdef_down"
                    or "mdef_down"
                    or "elemental_resistance_down";
        }

        private static double GetAtbEfficiencyMultiplier(int commandAtb, CharacterRole role)
        {
            var step = role == CharacterRole.DPS ? 0.06 : 0.12;
            var multiplier = 1.0 - ((commandAtb - 4) * step);
            return Math.Clamp(multiplier, 0.72, 1.12);
        }

        private static double GetUltimateInitialChargeTimingAdjustment(OwnedWeaponCandidate weapon, string slotName, double damageScore, double activeUtilityScore)
        {
            if (!slotName.Equals("Ultimate", StringComparison.OrdinalIgnoreCase)
                || !weapon.IsUltimate)
            {
                return 0d;
            }

            var timingFactor = Math.Clamp(0.08 - (weapon.Item.InitialChargeTimeSec * 0.012), -0.28, 0.08);
            if (Math.Abs(timingFactor) <= 0.001)
            {
                return 0d;
            }

            var weightedActiveValue = (damageScore * 0.25) + (activeUtilityScore * 0.65);
            if (weightedActiveValue <= 0.001)
            {
                return 0d;
            }

            return weightedActiveValue * timingFactor;
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
                var supportHasDebuffSetup = baseVariants.Any(variant =>
                {
                    var offensiveRole = EnsureOffensiveRoleInference(variant, request).Role;
                    return (variant.Role is CharacterRole.Support or CharacterRole.Healer || offensiveRole == OffensiveRoleProfile.OffensiveEnabler)
                        && variant.ProvidedEffectKeys.Overlaps(new[] { "elemental_resistance_down", "pdef_down", "mdef_down" });
                });
                if (supportHasDebuffSetup)
                {
                    bonus += 55 * referenceTuningProfile.SupportDebuffSetupRatio;
                }
                else if (CanAssumeStandardSynthDebuffSeedSetup(baseVariants))
                {
                    bonus += 9 * referenceTuningProfile.SupportDebuffSetupRatio;
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

        private static double GetWeaponBattleFitMultiplier(
            OwnedWeaponCandidate weapon,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request,
            string slotName,
            bool includeActiveEffects,
            bool includeDamage,
            IReadOnlyDictionary<string, int> passivePoints,
            IReadOnlyCollection<string> providedEffectKeys,
            IReadOnlyCollection<DetectedActiveEffect> detectedEffects,
            int patk,
            int matk,
            int heal)
        {
            var multiplier = 1.0;
            var isSubWeapon = slotName.Equals("Sub Weapon", StringComparison.OrdinalIgnoreCase);
            var hasOffHandSupportBridge = slotName.Equals("Off-hand", StringComparison.OrdinalIgnoreCase)
                && HasRelevantDpsOffHandSupportBridge(detectedEffects, request);

            if (role == CharacterRole.DPS && (includeActiveEffects || includeDamage))
            {
                if (request.PreferredDamageType != DamageType.Any
                    && !MatchesRequestedDamageType(weapon.Item.AbilityType, request.PreferredDamageType))
                {
                    multiplier *= hasOffHandSupportBridge ? 0.82 : 0.66;
                }

                if (request.EnemyWeakness != Element.None
                    && !weapon.Item.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                    && !MatchesRequestedElement(weapon.Item.Element, request.EnemyWeakness))
                {
                    multiplier *= 0.78;
                }
                else if (request.EnemyWeakness != Element.None
                    && weapon.Item.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                    && slotName.Equals("Main Weapon", StringComparison.OrdinalIgnoreCase))
                {
                    multiplier *= 0.62;
                }
            }

            var statPassiveFitMultiplier = GetWeaponStatPassiveFitMultiplier(passivePoints, providedEffectKeys, patk, matk, heal, role, request, isSubWeapon);
            multiplier *= statPassiveFitMultiplier;

            if (!isSubWeapon && role != CharacterRole.DPS)
            {
                multiplier *= GetNonDpsOffensiveEffectFitMultiplier(providedEffectKeys, request);
                multiplier *= GetNonDpsOffTargetElementResistDownFitMultiplier(providedEffectKeys, detectedEffects, request);
                multiplier *= GetNonDpsBroadDefensePracticalityMultiplier(providedEffectKeys, request, weapon.Item.CommandAtb);
            }

            return multiplier;
        }

        private static bool HasRelevantDpsOffHandSupportBridge(
            IReadOnlyCollection<DetectedActiveEffect> detectedEffects,
            PlayerPowerAnalyzerV2Request request)
        {
            if (detectedEffects.Count == 0)
            {
                return false;
            }

            var bridgeKeys = detectedEffects
                .Where(effect => effect.TargetScope is not ActiveEffectTargetScope.Self and not ActiveEffectTargetScope.Unknown)
                .Select(effect => effect.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (bridgeKeys.Count == 0)
            {
                return false;
            }

            return HasRelevantNonDpsOffensiveSignal(bridgeKeys, request);
        }

        private static double GetNonDpsOffensiveEffectFitMultiplier(IReadOnlyCollection<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request)
        {
            if (request.PreferredDamageType == DamageType.Any || providedEffectKeys.Count == 0)
            {
                return 1.0;
            }

            var hasPhysicalSupportSignal = providedEffectKeys.Any(key => key is "patk_up" or "pdef_down" or "phys_damage_bonus" or "phys_weapon_boost");
            var hasMagicalSupportSignal = providedEffectKeys.Any(key => key is "matk_up" or "mdef_down" or "mag_damage_bonus" or "mag_weapon_boost");

            if (request.PreferredDamageType == DamageType.Physical)
            {
                return hasMagicalSupportSignal && !hasPhysicalSupportSignal ? 0.18 : 1.0;
            }

            return hasPhysicalSupportSignal && !hasMagicalSupportSignal ? 0.18 : 1.0;
        }

        private static double GetNonDpsBroadDefensePracticalityMultiplier(IReadOnlyCollection<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request, int commandAtb)
        {
            if (request.PreferredDamageType == DamageType.Any
                || commandAtb < 5
                || !providedEffectKeys.Contains("pdef_up", StringComparer.OrdinalIgnoreCase)
                || !providedEffectKeys.Contains("mdef_up", StringComparer.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            if (HasRelevantNonDpsOffensiveSignal(providedEffectKeys, request))
            {
                return 0.9;
            }

            return 0.72;
        }

        private static double GetNonDpsOffTargetElementResistDownFitMultiplier(
            IReadOnlyCollection<string> providedEffectKeys,
            IReadOnlyCollection<DetectedActiveEffect> detectedEffects,
            PlayerPowerAnalyzerV2Request request)
        {
            if (request.EnemyWeakness == Element.None
                || detectedEffects.Count == 0
                || !providedEffectKeys.Contains("elemental_resistance_down", StringComparer.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            var requestedElement = request.EnemyWeakness.ToString();
            var matchingElementResDownCount = detectedEffects
                .Select(GetDetectedElementalResistanceDownTargetElement)
                .Where(targetElement => !string.IsNullOrWhiteSpace(targetElement)
                    && targetElement.Equals(requestedElement, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (matchingElementResDownCount > 0)
            {
                return 1.0;
            }

            var offTargetElementResDownCount = detectedEffects
                .Select(GetDetectedElementalResistanceDownTargetElement)
                .Where(targetElement => !string.IsNullOrWhiteSpace(targetElement)
                    && !targetElement.Equals(requestedElement, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (offTargetElementResDownCount == 0)
            {
                return 1.0;
            }

            var hasRequestedAxisSupportSignal = request.PreferredDamageType switch
            {
                DamageType.Physical => providedEffectKeys.Any(key => key is "patk_up" or "pdef_down" or "phys_damage_bonus" or "phys_weapon_boost" or "phys_damage_received_up" or "elemental_damage_up" or "elemental_damage_bonus" or "elemental_weapon_boost" or "elemental_damage_received_up"),
                DamageType.Magical => providedEffectKeys.Any(key => key is "matk_up" or "mdef_down" or "mag_damage_bonus" or "mag_weapon_boost" or "mag_damage_received_up" or "elemental_damage_up" or "elemental_damage_bonus" or "elemental_weapon_boost" or "elemental_damage_received_up"),
                _ => false
            };

            if (hasRequestedAxisSupportSignal)
            {
                return offTargetElementResDownCount > 1 ? 0.76 : 0.88;
            }

            return offTargetElementResDownCount > 1 ? 0.34 : 0.58;
        }

        private static bool HasRelevantNonDpsOffensiveSignal(IReadOnlyCollection<string> providedEffectKeys, PlayerPowerAnalyzerV2Request request)
        {
            if (providedEffectKeys.Contains("elemental_resistance_down", StringComparer.OrdinalIgnoreCase)
                || GetRelevantEnhancementKeys(request).Any(key => providedEffectKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                || providedEffectKeys.Contains("elemental_damage_bonus", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("elemental_weapon_boost", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("exploit_weakness", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("stat_buff_tier_increase", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("stat_debuff_tier_increase", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("atb_conservation", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("enfeeble", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("enliven", StringComparer.OrdinalIgnoreCase)
                || providedEffectKeys.Contains("torpor", StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return request.PreferredDamageType switch
            {
                DamageType.Physical => providedEffectKeys.Any(key => key is "patk_up" or "pdef_down" or "phys_damage_bonus" or "phys_weapon_boost"),
                DamageType.Magical => providedEffectKeys.Any(key => key is "matk_up" or "mdef_down" or "mag_damage_bonus" or "mag_weapon_boost"),
                _ => false
            };
        }

        private static double GetWeaponStatPassiveFitMultiplier(
            IReadOnlyDictionary<string, int> passivePoints,
            IReadOnlyCollection<string> providedEffectKeys,
            int patk,
            int matk,
            int heal,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request,
            bool isSubWeapon)
        {
            if (request.PreferredDamageType == DamageType.Any)
            {
                return 1.0;
            }

            var physicalPassivePoints = GetPassiveSignalPoints(passivePoints, "boost patk", "boost atk", "phys. ability pot");
            var magicalPassivePoints = GetPassiveSignalPoints(passivePoints, "boost matk", "boost atk", "mag. ability pot");
            var healingPassivePoints = GetPassiveSignalPoints(passivePoints, "heal", "cure spells");
            var multiplier = 1.0;
            var hasExtensionPassive = passivePoints.Keys.Any(skillName => IsBuffDebuffExtensionPassive(skillName));
            var hasHealerSupportPassiveBundle = passivePoints.Keys.Any(skillName => skillName.Contains("heal", StringComparison.OrdinalIgnoreCase)
                || skillName.Contains("cure", StringComparison.OrdinalIgnoreCase)
                || skillName.Contains("esuna", StringComparison.OrdinalIgnoreCase));
            // NOTE: "Interruption" is intentionally NOT treated as a persistent operational passive — it
            // only applies during a battle's sigil/interrupt phase, so it must not protect a weapon from
            // downweighting in a normal single-enemy build. ATB / Command Gauge are genuinely persistent.
            var hasTeamwideOperationalPassive = passivePoints.Keys.Any(skillName => skillName.Contains("All Allies", StringComparison.OrdinalIgnoreCase)
                && (skillName.Contains("ATB", StringComparison.OrdinalIgnoreCase)
                    || skillName.Contains("Command Gauge", StringComparison.OrdinalIgnoreCase)));
            var lacksRelevantNonDpsSupportSignal = role != CharacterRole.DPS
                && !HasRelevantNonDpsOffensiveSignal(providedEffectKeys, request);
            var hasPureHealingExtensionPassivePackage = hasHealerSupportPassiveBundle
                && physicalPassivePoints <= 0
                && magicalPassivePoints <= 0
                && !hasTeamwideOperationalPassive;

            if (request.PreferredDamageType == DamageType.Physical)
            {
                if (matk > (patk * 1.18) && heal < (matk * 0.6))
                {
                    multiplier *= isSubWeapon ? 0.72 : 0.86;
                }

                if (magicalPassivePoints > physicalPassivePoints + 5)
                {
                    multiplier *= isSubWeapon ? 0.68 : 0.82;
                }

                if (isSubWeapon
                    && role == CharacterRole.Healer
                    && healingPassivePoints > physicalPassivePoints + 5)
                {
                    multiplier *= 0.88;
                }

                if (isSubWeapon
                    && role != CharacterRole.DPS
                    && lacksRelevantNonDpsSupportSignal
                    && !hasExtensionPassive
                    && hasHealerSupportPassiveBundle)
                {
                    multiplier *= 0.42;
                }

                if (isSubWeapon
                    && role != CharacterRole.DPS
                    && lacksRelevantNonDpsSupportSignal
                    && hasPureHealingExtensionPassivePackage)
                {
                    multiplier *= 0.72;
                }

                if (lacksRelevantNonDpsSupportSignal && !hasExtensionPassive && healingPassivePoints > physicalPassivePoints + 5)
                {
                    multiplier *= isSubWeapon ? 0.56 : 0.82;
                }

                if (lacksRelevantNonDpsSupportSignal && !hasExtensionPassive && magicalPassivePoints > physicalPassivePoints + 5)
                {
                    multiplier *= isSubWeapon ? 0.62 : 0.84;
                }
            }
            else if (request.PreferredDamageType == DamageType.Magical)
            {
                if (patk > (matk * 1.18) && heal < (patk * 0.6))
                {
                    multiplier *= isSubWeapon ? 0.72 : 0.86;
                }

                if (physicalPassivePoints > magicalPassivePoints + 5)
                {
                    multiplier *= isSubWeapon ? 0.68 : 0.82;
                }

                if (isSubWeapon
                    && role == CharacterRole.Healer
                    && healingPassivePoints > magicalPassivePoints + 5)
                {
                    multiplier *= 0.88;
                }

                if (isSubWeapon
                    && role != CharacterRole.DPS
                    && lacksRelevantNonDpsSupportSignal
                    && !hasExtensionPassive
                    && hasHealerSupportPassiveBundle)
                {
                    multiplier *= 0.42;
                }

                if (isSubWeapon
                    && role != CharacterRole.DPS
                    && lacksRelevantNonDpsSupportSignal
                    && hasPureHealingExtensionPassivePackage)
                {
                    multiplier *= 0.72;
                }

                if (lacksRelevantNonDpsSupportSignal && !hasExtensionPassive && healingPassivePoints > magicalPassivePoints + 5)
                {
                    multiplier *= isSubWeapon ? 0.56 : 0.82;
                }

                if (lacksRelevantNonDpsSupportSignal && !hasExtensionPassive && physicalPassivePoints > magicalPassivePoints + 5)
                {
                    multiplier *= isSubWeapon ? 0.62 : 0.84;
                }
            }

            return multiplier;
        }

        private static int GetPassiveSignalPoints(IReadOnlyDictionary<string, int> passivePoints, params string[] signals)
        {
            return passivePoints
                .Where(kvp => signals.Any(signal => kvp.Key.Contains(signal, StringComparison.OrdinalIgnoreCase)))
                .Sum(kvp => kvp.Value);
        }

        private static double GetCostumeBattleFitMultiplier(OwnedCostumeCandidate costume, CharacterRole role, PlayerPowerAnalyzerV2Request request, string slotName)
        {
            if (!slotName.Equals("Sub Outfit", StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            var multiplier = 1.0;
            if (request.EnemyWeakness != Element.None
                && !string.IsNullOrWhiteSpace(costume.Item.Element)
                && !costume.Item.Element.Equals("None", StringComparison.OrdinalIgnoreCase)
                && !costume.Item.Element.Equals("Non-Elemental", StringComparison.OrdinalIgnoreCase)
                && !costume.Item.Element.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                multiplier *= 0.7;
            }

            if (!string.IsNullOrWhiteSpace(costume.Item.AbilityType)
                && request.PreferredDamageType != DamageType.Any
                && !costume.Item.AbilityType.Equals(request.PreferredDamageType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                multiplier *= role == CharacterRole.DPS ? 0.82 : 0.9;
            }

            return multiplier;
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

        private SelectedCustomization SelectBestCustomization(OwnedWeaponCandidate weapon, CharacterRole role, PlayerPowerAnalyzerV2Request request, ReferenceTuningProfile referenceTuningProfile, string slotName, double slotMultiplier, bool includeActiveEffects)
        {
            var best = new SelectedCustomization();
            var baseEffects = includeActiveEffects
                ? DetectActiveEffects(weapon.Item.EffectTags, weapon.Snapshot.AbilityText, request, request.BossImmunityKeys, slotName, weapon.Item.Name, weapon.Item.AbilityType, weapon.Item.Element)
                : Array.Empty<DetectedActiveEffect>();
            var baseActionHasThresholdSensitiveSetup = HasThresholdSensitiveSetupEffect(baseEffects);
            foreach (var customization in weapon.Snapshot.Customizations)
            {
                var passiveScore = 0d;
                if (!string.IsNullOrWhiteSpace(customization.PassiveSkillName) && customization.PassiveSkillPoints > 0)
                {
                    var appliedPoints = Math.Max(0, (int)Math.Floor(customization.PassiveSkillPoints * slotMultiplier));
                    foreach (var scoringLabel in GetPassiveScoringLabels(customization.PassiveSkillName, customization.PassiveEffects, preferResolvedEffectLabels: false))
                    {
                        passiveScore += ScorePassiveSkill(scoringLabel, appliedPoints, role, request);
                    }
                }

                var effectScore = 0d;
                if (includeActiveEffects && !string.IsNullOrWhiteSpace(customization.Description))
                {
                    var customizationEffects = DetectActiveEffects(Array.Empty<string>(), customization.Description, request, request.BossImmunityKeys, "Customization", weapon.Item.Name, weapon.Item.AbilityType, weapon.Item.Element);
                    var customizationHasThresholdSensitiveSetup = HasThresholdSensitiveSetupEffect(customizationEffects);
                    foreach (var effect in customizationEffects)
                    {
                        effectScore += GetCustomizationEffectDelta(
                            effect,
                            baseEffects,
                            weapon.Item.CommandAtb,
                            role,
                            request,
                            referenceTuningProfile,
                            slotName,
                            customizationHasThresholdSensitiveSetup,
                            baseActionHasThresholdSensitiveSetup,
                            out _);
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
                    PassiveEffects = customization.PassiveEffects,
                    EffectScore = effectScore,
                    Score = score
                };
            }

            return best;
        }

        private double GetCustomizationEffectDelta(
            DetectedActiveEffect customizationEffect,
            IReadOnlyList<DetectedActiveEffect> baseEffects,
            int commandAtb,
            CharacterRole role,
            PlayerPowerAnalyzerV2Request request,
            ReferenceTuningProfile referenceTuningProfile,
            string slotName,
            bool customizationHasThresholdSensitiveSetup,
            bool baseActionHasThresholdSensitiveSetup,
            out double rawEffectDelta)
        {
            var rawCustomizationScore = ScoreActiveEffectWithReferenceTuning(customizationEffect, role, request, referenceTuningProfile);
            var adjustedCustomizationScore = ApplyAtbEfficiencyToUtilityScore(rawCustomizationScore, commandAtb, customizationEffect, role, slotName, customizationHasThresholdSensitiveSetup);
            var baseEffect = FindComparableBaseEffect(baseEffects, customizationEffect);
            if (baseEffect == null)
            {
                rawEffectDelta = rawCustomizationScore;
                return adjustedCustomizationScore;
            }

            var rawBaseScore = ScoreActiveEffectWithReferenceTuning(baseEffect, role, request, referenceTuningProfile);
            var adjustedBaseScore = ApplyAtbEfficiencyToUtilityScore(rawBaseScore, commandAtb, baseEffect, role, slotName, baseActionHasThresholdSensitiveSetup);
            rawEffectDelta = Math.Max(0d, rawCustomizationScore - rawBaseScore);
            return Math.Max(0d, adjustedCustomizationScore - adjustedBaseScore);
        }

        private static DetectedActiveEffect? FindComparableBaseEffect(IReadOnlyList<DetectedActiveEffect> baseEffects, DetectedActiveEffect customizationEffect)
        {
            return baseEffects
                .Where(effect => string.Equals(effect.Key, customizationEffect.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(effect => effect.TargetScope == customizationEffect.TargetScope)
                .ThenByDescending(effect => string.Equals(effect.SourceAbilityType, customizationEffect.SourceAbilityType, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        private static IEnumerable<string> DetectEffectKeys(IEnumerable<string> effectTags, string abilityText, PlayerPowerAnalyzerV2Request request, IEnumerable<string> bossImmunityKeys)
        {
            return DetectActiveEffects(effectTags, abilityText, request, bossImmunityKeys, "Unknown", string.Empty, string.Empty, string.Empty)
                .Select(effect => effect.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSelfOnlyTargeting(string sourceText)
        {
            return ContainsRangeMarker(sourceText, "Self");
        }

        private static bool IsAllAlliesTargeting(string sourceText)
        {
            return ContainsRangeMarker(sourceText, "All Allies");
        }

        private static bool IsOtherAlliesTargeting(string sourceText)
        {
            return ContainsRangeMarker(sourceText, "Other Allies");
        }

        private static bool IsSingleAllyTargeting(string sourceText)
        {
            return ContainsRangeMarker(sourceText, "Single Ally");
        }

        private static bool ContainsRangeMarker(string sourceText, string rangeLabel)
        {
            return (sourceText ?? string.Empty).Contains($"[Rng.: {rangeLabel}]", StringComparison.OrdinalIgnoreCase)
                || (sourceText ?? string.Empty).Contains($"[Rng: {rangeLabel}]", StringComparison.OrdinalIgnoreCase)
                || (sourceText ?? string.Empty).Contains($"Range: {rangeLabel}", StringComparison.OrdinalIgnoreCase)
                || (sourceText ?? string.Empty).Contains($"Rng.: {rangeLabel}", StringComparison.OrdinalIgnoreCase)
                || (sourceText ?? string.Empty).Contains($"Rng: {rangeLabel}", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesRequestedDamageType(string abilityType, DamageType preferredDamageType)
        {
            if (preferredDamageType == DamageType.Any || string.IsNullOrWhiteSpace(abilityType))
            {
                return true;
            }

            var normalized = abilityType.ToLowerInvariant();
            return preferredDamageType == DamageType.Physical
                ? normalized.Contains("phys", StringComparison.OrdinalIgnoreCase)
                : normalized.Contains("mag", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesRequestedElement(string element, Element requestedElement)
        {
            if (requestedElement == Element.None || string.IsNullOrWhiteSpace(element))
            {
                return true;
            }

            return element.Equals(requestedElement.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetDetectedElementalResistanceDownTargetElement(DetectedActiveEffect effect)
        {
            if (!effect.Key.Equals("elemental_resistance_down", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(effect.SourceText))
            {
                return null;
            }

            foreach (var element in Enum.GetValues<Element>())
            {
                if (element == Element.None)
                {
                    continue;
                }

                var elementName = element.ToString();
                if (effect.SourceText.Contains($"{elementName} Resistance Down", StringComparison.OrdinalIgnoreCase)
                    || effect.SourceText.Contains($"{elementName} Weakness", StringComparison.OrdinalIgnoreCase))
                {
                    return elementName;
                }
            }

            return null;
        }

        private static bool IsRequestedElementalResistanceDownEffect(DetectedActiveEffect effect, PlayerPowerAnalyzerV2Request request)
        {
            var targetElement = GetDetectedElementalResistanceDownTargetElement(effect);
            return request.EnemyWeakness != Element.None
                && !string.IsNullOrWhiteSpace(targetElement)
                && targetElement.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOffAxisElementalResistanceDownEffect(DetectedActiveEffect effect, PlayerPowerAnalyzerV2Request request)
        {
            var targetElement = GetDetectedElementalResistanceDownTargetElement(effect);
            return request.EnemyWeakness != Element.None
                && !string.IsNullOrWhiteSpace(targetElement)
                && !targetElement.Equals(request.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase);
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
            public List<PassiveSkillEffectDetail> PassiveEffects { get; set; } = new();
            public double EffectScore { get; set; }
            public double Score { get; set; }
        }

        private sealed class SearchModeFilterResult
        {
            public List<OwnedWeaponCandidate> Weapons { get; set; } = new();
            public int TrimmedWeaponCount { get; set; }
            public int AffectedCharacterCount { get; set; }
        }

        private sealed class AdaptiveSearchProfile
        {
            public int MainWeaponOptionsPerCharacter { get; set; }
            public int OffHandWeaponOptionsPerCharacter { get; set; }
            public int UltimateOptionsPerCharacter { get; set; }
            public int MainOutfitOptionsPerCharacter { get; set; }
            public int SubOutfitOptionsPerCharacter { get; set; }
            public int RetainedVariantsPerCharacter { get; set; }
            public int SkeletonExpansionLimit { get; set; }
            public int CharacterShortlistLimit { get; set; }
            public bool IsVariantBreadthReduced => MainWeaponOptionsPerCharacter < DefaultMainWeaponOptionsPerCharacter
                || OffHandWeaponOptionsPerCharacter < DefaultOffHandWeaponOptionsPerCharacter
                || UltimateOptionsPerCharacter < DefaultUltimateOptionsPerCharacter
                || MainOutfitOptionsPerCharacter < DefaultMainOutfitOptionsPerCharacter
                || SubOutfitOptionsPerCharacter < DefaultSubOutfitOptionsPerCharacter
                || RetainedVariantsPerCharacter < DefaultRetainedVariantsPerCharacter;
        }

        private sealed class AdaptiveCharacterShortlistResult
        {
            public Dictionary<string, List<CharacterBuildCandidate>> VariantsByCharacter { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public int OriginalCharacterCount { get; set; }
            public int RetainedCharacterCount { get; set; }
            public bool WasShortlisted { get; set; }
        }

        private sealed class AdaptiveCharacterCandidate
        {
            public string CharacterName { get; set; } = string.Empty;
            public CharacterRole Role { get; set; }
            public double BestVariantSelectionScore { get; set; }
            public HashSet<string> ProvidedEffectKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class AnchorScoringContext
        {
            public string CharacterName { get; set; } = string.Empty;
            public string MainWeaponName { get; set; } = string.Empty;
            public double CandidateScore { get; set; }
            public HashSet<string> ActiveSourceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> MissingPriorityKeys { get; set; } = new();
        }

        private sealed class AnchorSupportScoreResult
        {
            public AnchorScoringContext? AnchorContext { get; set; }
            public double Score { get; set; }
            public List<string> CoveredPriorityLabels { get; set; } = new();
            public List<string> AmplifierLabels { get; set; } = new();
        }

        private sealed class OptimisticSubWeaponGain
        {
            public string WeaponName { get; set; } = string.Empty;
            public double Gain { get; set; }
            public double EvaluationScore { get; set; }
        }

        private sealed class SlotEvaluation
        {
            public string Name { get; set; } = string.Empty;
            public PlayerPowerAnalyzerV2ItemSlot Slot { get; set; } = new();
            public double BattleFitMultiplier { get; set; } = 1.0;
            public Dictionary<string, int> PassivePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> ProvidedEffectKeys { get; set; } = new();
            public double NonPassiveScore { get; set; }

            // Active-buff utility this slot contributes, broken out by effect family (post battle-fit).
            // The sum of these scores is the portion of NonPassiveScore that is subject to build-level
            // duplicate-buff deduplication; the remainder (stats, damage, timing) stays additive.
            public Dictionary<string, SlotActiveFamilyContribution> ActiveUtilityByFamily { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            public List<PlayerPowerAnalyzerV2ScoreComponent> ScoreBreakdown { get; set; } = new();
            public double Score { get; set; }
        }

        // A single weapon/outfit slot's active-buff contribution within one effect family. Used to
        // deduplicate overlapping buffs across the main/off-hand/ultimate/outfit slots: two equipped
        // sources of the same buff share a cap in battle, so the second is only worth its marginal
        // ramp/refresh value, not full value.
        private sealed class SlotActiveFamilyContribution
        {
            public double Score { get; set; }
            public int PotencyTier { get; set; }
            public int DesiredTier { get; set; }
        }

        private sealed class CharacterBuildCandidate
        {
            public string CharacterName { get; set; } = string.Empty;
            public CharacterRole Role { get; set; }
            public CharacterRole EffectiveSubWeaponRole { get; set; }
            public string EffectiveSubWeaponRoleReason { get; set; } = string.Empty;
            public OffensiveRoleProfile OffensiveRole { get; set; }
            public string OffensiveRoleReason { get; set; } = string.Empty;
            public double EstimatedDirectOffenseScore { get; set; }
            public double EstimatedOffensiveSupportScore { get; set; }
            public double EstimatedLowActualUsePenalty { get; set; }
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

            // This character's net active-buff contribution per effect family, after the character's own
            // (intra-character) duplicate-buff dedup. Used at team scoring to dedup the same buff across
            // party members: if two characters both provide a family, the weaker source is discounted to
            // its marginal value team-wide, just as duplicate buffs are within a single character.
            public Dictionary<string, SlotActiveFamilyContribution> ActiveFamilyContributions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            public List<PlayerPowerAnalyzerV2ScoreComponent> ScoreBreakdown { get; set; } = new();
            public IReadOnlyList<DetectedActiveEffect>? CachedDetectedEffects { get; set; }
            public double? SelectionScoreOverride { get; set; }

            public PlayerPowerAnalyzerV2CharacterBuild ToOutput()
            {
                var patk = MainWeapon.Patk + (OffHandWeapon?.Patk ?? 0) + (UltimateWeapon?.Patk ?? 0) + SubOutfits.Sum(x => x.Patk);
                var matk = MainWeapon.Matk + (OffHandWeapon?.Matk ?? 0) + (UltimateWeapon?.Matk ?? 0) + SubOutfits.Sum(x => x.Matk);
                var heal = MainWeapon.Heal + (OffHandWeapon?.Heal ?? 0) + (UltimateWeapon?.Heal ?? 0) + SubOutfits.Sum(x => x.Heal);
                return new PlayerPowerAnalyzerV2CharacterBuild
                {
                    CharacterName = CharacterName,
                    Role = Role,
                    EffectiveSubWeaponRole = EffectiveSubWeaponRole,
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
                    RecommendedMateria = new List<PlayerPowerAnalyzerV2MateriaRecommendation>(),
                    ProvidedEffectLabels = ProvidedEffectKeys.Select(ToLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    DebugNotes = new[]
                    {
                        string.IsNullOrWhiteSpace(EffectiveSubWeaponRoleReason) ? null : $"Effective sub-weapon role: {EffectiveSubWeaponRole} ({EffectiveSubWeaponRoleReason})",
                        string.IsNullOrWhiteSpace(OffensiveRoleReason) ? null : $"Offensive role: {OffensiveRole} ({OffensiveRoleReason})"
                    }
                        .Where(note => !string.IsNullOrWhiteSpace(note))
                        .Cast<string>()
                        .ToList(),
                    ScoreBreakdown = CloneScoreBreakdown(ScoreBreakdown)
                };
            }
        }

        private sealed class OffensiveRoleInferenceResult
        {
            public OffensiveRoleProfile Role { get; set; }
            public string Reason { get; set; } = string.Empty;
            public double DirectPressure { get; set; }
            public double EnablerPressure { get; set; }
            public double LowActualUsePenalty { get; set; }
        }

        private sealed class OffensiveShellScoreResult
        {
            public double Score { get; set; }
            public List<string> Notes { get; set; } = new();
        }

        private sealed class EffectiveSubWeaponRoleInference
        {
            public CharacterRole Role { get; set; }
            public string Reason { get; set; } = string.Empty;
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
            public string? OffensiveAbilitySummary { get; set; }
            public double Score { get; set; }
            public List<string> MatchedRequiredEffects { get; set; } = new();
            public List<string> MissingRequiredEffects { get; set; } = new();
            public List<string> MatchedPreferredEffects { get; set; } = new();
            public List<string> MissingPreferredEffects { get; set; } = new();
            public List<string> ProvidedEffectKeys { get; set; } = new();
            public List<string> SuppressedEffectNotes { get; set; } = new();
            public List<string> DebugNotes { get; set; } = new();
            public List<PlayerPowerAnalyzerV2ScoreComponent> ScoreBreakdown { get; set; } = new();
            public string TeamKey { get; set; } = string.Empty;
            public string EquipmentKey { get; set; } = string.Empty;
        }

        private sealed class TeamSkeleton
        {
            public List<CharacterBuildCandidate> SeedVariants { get; set; } = new();
            public double Score { get; set; }
            public string TeamKey { get; set; } = string.Empty;
            public string EquipmentKey { get; set; } = string.Empty;
            public string AnchorCharacterName { get; set; } = string.Empty;
            public string AnchorWeaponName { get; set; } = string.Empty;
        }
    }
}

