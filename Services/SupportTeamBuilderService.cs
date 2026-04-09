using System;
using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class SupportTeamBuilderService
    {
        private readonly WeaponSearchDataService _weaponSearchDataService;
        private static readonly HashSet<string> ManualStatusEffectsWithoutPotency = new(StringComparer.OrdinalIgnoreCase)
        {
            "All-Tgt. Phys. Dmg. Rcvd. Up",
            "Amp. Mag. Abilities",
            "Amp. Phys. Abilities",
            "Earth Damage Bonus",
            "Earth Weakness",
            "Earth Weapon Boost",
            "Exploit Weakness",
            "Fire Damage Bonus",
            "Fire Weakness",
            "Fire Weapon Boost",
            "Ice Damage Bonus",
            "Ice Weakness",
            "Ice Weapon Boost",
            "Lightning Damage Bonus",
            "Lightning Weapon Boost",
            "Mag. Damage Bonus",
            "Mag. Weapon Boost",
            "Magic Resistance Increased",
            "Overspeed Gauge",
            "Phys. Damage Bonus",
            "Phys. Weapon Boost",
            "Physical Resistance Increased",
            "Regen",
            "Single-Tgt. Mag. Dmg. Rcvd. Up",
            "Single-Tgt. Phys. Dmg. Rcvd. Up",
            "Torpor",
            "Veil",
            "Water Damage Bonus",
            "Water Weakness",
            "Water Weapon Boost"
        };

        public SupportTeamBuilderService(WeaponSearchDataService weaponSearchDataService)
        {
            _weaponSearchDataService = weaponSearchDataService;
        }

        public SupportTeamBuilderOptionData GetOptionData()
        {
            var entries = _weaponSearchDataService
                .GetWeapons()
                .ToList();

            var effectTypes = entries
                .SelectMany(w => w.EffectTags)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var effectHasPotency = effectTypes.ToDictionary(effect => effect, _ => false, StringComparer.OrdinalIgnoreCase);
            foreach (var effect in effectTypes)
            {
                if (IsManualStatusEffectWithoutPotency(effect))
                {
                    continue;
                }

                if (entries.Any(entry => ExtractEffectLineCandidates(entry.AbilityText, effect).Any(c => c.HasExplicitPotency)))
                {
                    effectHasPotency[effect] = true;
                }
            }

            return new SupportTeamBuilderOptionData
            {
                EffectTypes = effectTypes,
                Characters = entries
                    .Select(w => w.Character)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                EffectHasPotency = effectHasPotency
            };
        }

        public SupportTeamBuilderResponse Search(SupportTeamRequest request)
        {
            var filters = request.Filters
                .Where(f => f.IsValid)
                .DistinctBy(f => $"{f.EffectType}|{f.Range}|{f.MinBasePotency}|{f.MinMaxPotency}")
                .ToList();

            if (filters.Count == 0)
            {
                return new SupportTeamBuilderResponse();
            }

            var allEntries = _weaponSearchDataService
                .GetWeapons()
                .ToList();

            var weapons = allEntries
                .Where(w => !w.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var outfits = allEntries
                .Where(w => w.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var filterResults = filters
                .Select(filter => new SupportFilterResult
                {
                    Filter = filter,
                    MatchingWeapons = FindMatchingWeapons(filter, weapons, request),
                    MatchingOutfits = FindMatchingOutfits(filter, outfits, request)
                })
                .ToList();
            var teams = BuildTeams(filterResults, request.MaxCharacterCount, request.ExcludeCharacters);
            teams = FilterMustHaveCharacters(teams, request.MustHaveCharacters);
            teams = FilterDuplicates(teams);

            return new SupportTeamBuilderResponse
            {
                FilterResults = filterResults,
                Teams = teams
            };
        }

        private List<SupportWeaponMatch> FindMatchingWeapons(SupportTeamFilter filter, List<WeaponSearchItem> weapons, SupportTeamRequest request)
        {
            var matching = new List<SupportWeaponMatch>();

            foreach (var weapon in weapons)
            {
                if (!weapon.EffectTags.Any(t => t.Equals(filter.EffectType, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var selectedOb = ResolveOwnedObSelection(weapon.Id, request.OwnedObByWeaponId);
                var snapshot = _weaponSearchDataService.GetWeaponSnapshot(weapon.Id, SelectionToOverboost(selectedOb), 130);
                var abilityText = snapshot?.AbilityText ?? weapon.AbilityText;
                var effectCandidates = ExtractEffectLineCandidates(abilityText, filter.EffectType);
                var candidate = effectCandidates
                    .Where(c => MatchesRange(filter.Range, c.Range))
                    .FirstOrDefault(c => c.BasePotency >= filter.MinBasePotency && c.MaxPotency >= filter.MinMaxPotency);

                if (candidate == default)
                {
                    continue;
                }

                matching.Add(new SupportWeaponMatch
                {
                    Weapon = weapon,
                    WeaponId = weapon.Id,
                    WeaponName = weapon.Name,
                    Character = weapon.Character,
                    Range = candidate.Range,
                    AbilityText = abilityText,
                    BasePotency = candidate.BasePotency,
                    MaxPotency = candidate.MaxPotency,
                    OwnedObSelection = selectedOb
                });
            }

            return matching;
        }

        private static List<SupportOutfitMatch> FindMatchingOutfits(SupportTeamFilter filter, List<WeaponSearchItem> outfits, SupportTeamRequest request)
        {
            var matching = new List<SupportOutfitMatch>();

            foreach (var outfit in outfits)
            {
                if (!outfit.EffectTags.Any(t => t.Equals(filter.EffectType, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var selected = ResolveOwnedOutfitSelection(outfit.Id, request.OwnedOutfitById);
                var abilityText = outfit.AbilityText;
                var effectCandidates = ExtractEffectLineCandidates(abilityText, filter.EffectType);
                var candidate = effectCandidates
                    .Where(c => MatchesRange(filter.Range, c.Range))
                    .FirstOrDefault(c => c.BasePotency >= filter.MinBasePotency && c.MaxPotency >= filter.MinMaxPotency);

                if (candidate == default)
                {
                    continue;
                }

                matching.Add(new SupportOutfitMatch
                {
                    Outfit = outfit,
                    OutfitId = outfit.Id,
                    OutfitName = outfit.Name,
                    Character = outfit.Character,
                    Range = candidate.Range,
                    AbilityText = abilityText,
                    BasePotency = candidate.BasePotency,
                    MaxPotency = candidate.MaxPotency,
                    OwnedSelection = selected
                });
            }

            return matching;
        }

        private List<SupportTeamResult> BuildTeams(List<SupportFilterResult> filterResults, int maxCharacterCount, HashSet<string> excludeCharacters)
        {
            var teams = new List<SupportTeamResult> { new() };

            foreach (var filterResult in filterResults.AsEnumerable().Reverse())
            {
                var nextTeams = new List<SupportTeamResult>();

                foreach (var weaponMatch in filterResult.MatchingWeapons)
                {
                    if (weaponMatch.OwnedObSelection == 0)
                    {
                        continue;
                    }

                    if (excludeCharacters.Contains(weaponMatch.Character))
                    {
                        continue;
                    }

                    foreach (var existingTeam in teams)
                    {
                        var assigned = AssignWeapon(maxCharacterCount, filterResult.Filter, weaponMatch, existingTeam);
                        if (assigned != null)
                        {
                            nextTeams.Add(assigned);
                        }
                    }
                }

                foreach (var outfitMatch in filterResult.MatchingOutfits)
                {
                    if (outfitMatch.OwnedSelection == 0)
                    {
                        continue;
                    }

                    if (excludeCharacters.Contains(outfitMatch.Character))
                    {
                        continue;
                    }

                    foreach (var existingTeam in teams)
                    {
                        var assigned = AssignOutfit(maxCharacterCount, filterResult.Filter, outfitMatch, existingTeam);
                        if (assigned != null)
                        {
                            nextTeams.Add(assigned);
                        }
                    }
                }

                teams = nextTeams;
            }

            foreach (var team in teams)
            {
                ScoreTeam(team);
            }

            return teams
                .OrderByDescending(t => t.MaxPotenciesScore)
                .ThenByDescending(t => t.CharacterCountScore)
                .ThenByDescending(t => t.WeaponCountScore)
                .ThenByDescending(t => t.BasePotenciesScore)
                .ToList();
        }

        private static SupportTeamResult? AssignWeapon(int maxCharacterCount, SupportTeamFilter filter, SupportWeaponMatch weaponMatch, SupportTeamResult existingTeam)
        {
            var cloned = CloneTeam(existingTeam);
            var charKey = weaponMatch.Character;

            if (!cloned.Characters.TryGetValue(charKey, out var character))
            {
                if (cloned.Characters.Count >= maxCharacterCount)
                {
                    return null;
                }

                cloned.Characters[charKey] = new SupportCharacterAssignment
                {
                    Name = weaponMatch.Character,
                    MainHand = CreateEquippedWeapon(filter, weaponMatch)
                };

                return cloned;
            }

            if (character.MainHand == null)
            {
                character.MainHand = CreateEquippedWeapon(filter, weaponMatch);
                return cloned;
            }

            if (character.MainHand.Weapon.Name.Equals(weaponMatch.WeaponName, StringComparison.OrdinalIgnoreCase))
            {
                character.MainHand.MatchedFilters.Add(filter);
                return cloned;
            }

            if (character.OffHand != null)
            {
                if (character.OffHand.Weapon.Name.Equals(weaponMatch.WeaponName, StringComparison.OrdinalIgnoreCase))
                {
                    character.OffHand.MatchedFilters.Add(filter);
                    return cloned;
                }

                return null;
            }

            character.OffHand = CreateEquippedWeapon(filter, weaponMatch);
            return cloned;
        }

        private static SupportEquippedWeapon CreateEquippedWeapon(SupportTeamFilter filter, SupportWeaponMatch weaponMatch)
        {
            return new SupportEquippedWeapon
            {
                Weapon = weaponMatch.Weapon,
                OwnedObSelection = weaponMatch.OwnedObSelection,
                MatchedFilters = new List<SupportTeamFilter> { filter },
                BasePotency = weaponMatch.BasePotency,
                MaxPotency = weaponMatch.MaxPotency
            };
        }

        private static SupportTeamResult? AssignOutfit(int maxCharacterCount, SupportTeamFilter filter, SupportOutfitMatch outfitMatch, SupportTeamResult existingTeam)
        {
            var cloned = CloneTeam(existingTeam);
            var charKey = outfitMatch.Character;

            if (!cloned.Characters.TryGetValue(charKey, out var character))
            {
                if (cloned.Characters.Count >= maxCharacterCount)
                {
                    return null;
                }

                cloned.Characters[charKey] = new SupportCharacterAssignment
                {
                    Name = outfitMatch.Character,
                    Outfit = CreateEquippedOutfit(filter, outfitMatch)
                };

                return cloned;
            }

            if (character.Outfit == null)
            {
                character.Outfit = CreateEquippedOutfit(filter, outfitMatch);
                return cloned;
            }

            if (character.Outfit.Outfit.Name.Equals(outfitMatch.OutfitName, StringComparison.OrdinalIgnoreCase))
            {
                character.Outfit.MatchedFilters.Add(filter);
                return cloned;
            }

            return null;
        }

        private static SupportEquippedOutfit CreateEquippedOutfit(SupportTeamFilter filter, SupportOutfitMatch outfitMatch)
        {
            return new SupportEquippedOutfit
            {
                Outfit = outfitMatch.Outfit,
                OwnedSelection = outfitMatch.OwnedSelection,
                MatchedFilters = new List<SupportTeamFilter> { filter },
                BasePotency = outfitMatch.BasePotency,
                MaxPotency = outfitMatch.MaxPotency
            };
        }

        private static void ScoreTeam(SupportTeamResult team)
        {
            var equipped = team.GetEquippedWeapons().ToList();
            var equippedOutfits = team.GetEquippedOutfits().ToList();
            team.MaxPotenciesScore = equipped.Sum(w => (int)w.MaxPotency) + equippedOutfits.Sum(o => (int)o.MaxPotency);
            team.BasePotenciesScore = equipped.Sum(w => (int)w.BasePotency) + equippedOutfits.Sum(o => (int)o.BasePotency);
            team.CharacterCountScore = -team.Characters.Count;
            team.WeaponCountScore = -equipped.Count;
        }

        private static List<SupportTeamResult> FilterMustHaveCharacters(List<SupportTeamResult> teams, HashSet<string> mustHaveCharacters)
        {
            if (mustHaveCharacters.Count == 0)
            {
                return teams;
            }

            return teams
                .Where(team => mustHaveCharacters.All(m => team.Characters.Keys.Contains(m, StringComparer.OrdinalIgnoreCase)))
                .ToList();
        }

        private static List<SupportTeamResult> FilterDuplicates(List<SupportTeamResult> teams)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var output = new List<SupportTeamResult>();

            foreach (var team in teams)
            {
                var weaponKey = string.Join("|", team.GetWeaponNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                var outfitKey = string.Join("|", team.GetOutfitNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                var key = $"{weaponKey}||{outfitKey}";
                if (seen.Add(key))
                {
                    output.Add(team);
                }
            }

            return output;
        }

        private static SupportTeamResult CloneTeam(SupportTeamResult source)
        {
            var clone = new SupportTeamResult();
            foreach (var kvp in source.Characters)
            {
                clone.Characters[kvp.Key] = new SupportCharacterAssignment
                {
                    Name = kvp.Value.Name,
                    MainHand = kvp.Value.MainHand == null ? null : CloneWeapon(kvp.Value.MainHand),
                    OffHand = kvp.Value.OffHand == null ? null : CloneWeapon(kvp.Value.OffHand),
                    Outfit = kvp.Value.Outfit == null ? null : CloneOutfit(kvp.Value.Outfit)
                };
            }

            return clone;
        }

        private static SupportEquippedWeapon CloneWeapon(SupportEquippedWeapon source)
        {
            return new SupportEquippedWeapon
            {
                Weapon = source.Weapon,
                OwnedObSelection = source.OwnedObSelection,
                MatchedFilters = source.MatchedFilters.ToList(),
                BasePotency = source.BasePotency,
                MaxPotency = source.MaxPotency
            };
        }

        private static SupportEquippedOutfit CloneOutfit(SupportEquippedOutfit source)
        {
            return new SupportEquippedOutfit
            {
                Outfit = source.Outfit,
                OwnedSelection = source.OwnedSelection,
                MatchedFilters = source.MatchedFilters.ToList(),
                BasePotency = source.BasePotency,
                MaxPotency = source.MaxPotency
            };
        }

        private static bool MatchesRange(SupportFilterRange filterRange, string weaponRange)
        {
            var range = NormalizeRange(weaponRange);
            return filterRange switch
            {
                SupportFilterRange.All => range == "all",
                SupportFilterRange.SingleTargetOrAll => range == "all" || range == "single",
                SupportFilterRange.SelfOrSingleTargetOrAll => range == "all" || range == "single" || range == "self",
                _ => false
            };
        }

        private static string NormalizeRange(string range)
        {
            var normalized = (range ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Contains("single"))
            {
                return "single";
            }

            if (normalized.Contains("self"))
            {
                return "self";
            }

            return "all";
        }

        private static int ResolveOwnedObSelection(string weaponId, Dictionary<string, int> selected)
        {
            if (selected.TryGetValue(weaponId, out var value))
            {
                return Math.Clamp(value, 0, 4);
            }

            return 4;
        }

        private static int ResolveOwnedOutfitSelection(string outfitId, Dictionary<string, int> selected)
        {
            if (selected.TryGetValue(outfitId, out var value))
            {
                return Math.Clamp(value, 0, 1);
            }

            return 1;
        }

        private static int SelectionToOverboost(int selection)
        {
            return selection switch
            {
                1 => 0,
                2 => 1,
                3 => 6,
                4 => 10,
                _ => 0
            };
        }

        private static (SupportPotencyTier Base, SupportPotencyTier Max) ParsePotencies(string text)
        {
            var input = text ?? string.Empty;
            var tiers = new List<SupportPotencyTier>();
            if (input.Contains("Extra High", StringComparison.OrdinalIgnoreCase)) tiers.Add(SupportPotencyTier.ExtraHigh);
            if (input.Contains("High", StringComparison.OrdinalIgnoreCase)) tiers.Add(SupportPotencyTier.High);
            if (input.Contains("Mid", StringComparison.OrdinalIgnoreCase)) tiers.Add(SupportPotencyTier.Mid);
            if (input.Contains("Low", StringComparison.OrdinalIgnoreCase)) tiers.Add(SupportPotencyTier.Low);

            if (tiers.Count == 0)
            {
                return (SupportPotencyTier.Low, SupportPotencyTier.Low);
            }

            return (tiers.Min(), tiers.Max());
        }

        private static List<EffectLineCandidate> ExtractEffectLineCandidates(string abilityText, string effectType)
        {
            var candidates = new List<EffectLineCandidate>();
            if (string.IsNullOrWhiteSpace(abilityText) || string.IsNullOrWhiteSpace(effectType))
            {
                return candidates;
            }

            var lines = abilityText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            foreach (var line in lines)
            {
                if (!line.Contains(effectType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var range = ExtractBracketValue(line, "Rng.") ?? ExtractBracketValue(line, "Range");
                if (string.IsNullOrWhiteSpace(range))
                {
                    continue;
                }

                var treatAsStatusEffect = IsManualStatusEffectWithoutPotency(effectType);
                var basePotRaw = treatAsStatusEffect ? null : ExtractBracketValue(line, "Pot");
                var maxPotRaw = treatAsStatusEffect ? null : ExtractBracketValue(line, "Max Pot");
                var basePotText = basePotRaw ?? "Low";
                var maxPotText = maxPotRaw ?? basePotText;

                candidates.Add(new EffectLineCandidate
                {
                    Range = range,
                    BasePotency = ParsePotencyToken(basePotText),
                    MaxPotency = ParsePotencyToken(maxPotText),
                    HasExplicitPotency = basePotRaw != null || maxPotRaw != null
                });
            }

            return candidates;
        }

        private static bool IsManualStatusEffectWithoutPotency(string effectType)
        {
            return ManualStatusEffectsWithoutPotency.Contains(effectType);
        }

        private static string? ExtractBracketValue(string line, string label)
        {
            var start = line.IndexOf($"[{label}:", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            var valueStart = start + label.Length + 2;
            var valueEnd = line.IndexOf(']', valueStart);
            if (valueEnd < 0)
            {
                return null;
            }

            var value = line[valueStart..valueEnd].Trim();
            return value.Length == 0 ? null : value;
        }

        private static SupportPotencyTier ParsePotencyToken(string token)
        {
            if (token.Contains("Extra High", StringComparison.OrdinalIgnoreCase)) return SupportPotencyTier.ExtraHigh;
            if (token.Contains("High", StringComparison.OrdinalIgnoreCase)) return SupportPotencyTier.High;
            if (token.Contains("Mid", StringComparison.OrdinalIgnoreCase)) return SupportPotencyTier.Mid;
            return SupportPotencyTier.Low;
        }

        private readonly record struct EffectLineCandidate
        {
            public string Range { get; init; }
            public SupportPotencyTier BasePotency { get; init; }
            public SupportPotencyTier MaxPotency { get; init; }
            public bool HasExplicitPotency { get; init; }
        }
    }
}
