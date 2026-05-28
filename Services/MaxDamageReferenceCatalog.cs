using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;
using Microsoft.AspNetCore.Hosting;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class MaxDamageReferenceCatalog
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Dictionary<string, string> CharacterAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sephiorth (Original)"] = "Sephiroth (Original)"
        };

        private static readonly Dictionary<string, string> ItemAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Devine Emissary"] = "Divine Emissary",
            ["Guantlets of the Worthy"] = "Gauntlets of the Worthy",
            ["Conqueror of the PLanet"] = "Conqueror of the Planet",
            ["CLassic Dress"] = "Classic Dress",
            ["Erdrick' Armour"] = "Erdrick's Armour",
            ["Shing Spirit Beachwear"] = "Shin Spirit Beachwear"
        };

        private readonly IWebHostEnvironment _env;
        private readonly WeaponSearchDataService _weaponSearchDataService;
        private MaxDamageReferenceConfiguration _configuration = new();
        private List<MaxDamageReferenceTeamSummary> _teamSummaries = new();
        private List<MaxDamageReferenceArchetypeSummary> _archetypeSummaries = new();

        public MaxDamageReferenceCatalog(IWebHostEnvironment env, WeaponSearchDataService weaponSearchDataService)
        {
            _env = env;
            _weaponSearchDataService = weaponSearchDataService;
            Load();
        }

        public MaxDamageReferenceConfiguration GetConfiguration()
        {
            return _configuration;
        }

        public List<MaxDamageReferenceTeamSummary> GetTeamSummaries()
        {
            return _teamSummaries.ToList();
        }

        public List<MaxDamageReferenceArchetypeSummary> GetArchetypeSummaries()
        {
            return _archetypeSummaries.ToList();
        }

        public MaxDamageReferenceMatchResult? FindClosestMatch(Element weakness, DamageType preferredDamageType, IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characters, IEnumerable<string> providedEffectKeys)
        {
            var availableSummaries = _teamSummaries
                .Where(summary => summary.EnemyWeakness == weakness && summary.PreferredDamageType == preferredDamageType)
                .ToList();

            if (availableSummaries.Count == 0)
            {
                availableSummaries = _teamSummaries
                    .Where(summary => summary.EnemyWeakness == weakness || summary.PreferredDamageType == preferredDamageType)
                    .ToList();
            }

            if (availableSummaries.Count == 0)
            {
                availableSummaries = _teamSummaries.ToList();
            }

            var result = availableSummaries
                .Select(summary => BuildMatchResult(summary, weakness, preferredDamageType, characters, providedEffectKeys))
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.ArchetypeId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return result?.Score > 0 ? result : null;
        }

        private void Load()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "maxDamageReferenceTeams.json");
            if (!File.Exists(path))
            {
                _configuration = new MaxDamageReferenceConfiguration();
                _teamSummaries = new List<MaxDamageReferenceTeamSummary>();
                _archetypeSummaries = new List<MaxDamageReferenceArchetypeSummary>();
                return;
            }

            var json = File.ReadAllText(path);
            _configuration = JsonSerializer.Deserialize<MaxDamageReferenceConfiguration>(json, JsonOptions) ?? new MaxDamageReferenceConfiguration();
            _teamSummaries = _configuration.Teams
                .Where(team => !string.IsNullOrWhiteSpace(team.Id) && !string.IsNullOrWhiteSpace(team.ArchetypeId) && team.Characters.Count > 0)
                .Select(SummarizeTeam)
                .ToList();
            _archetypeSummaries = _teamSummaries
                .GroupBy(summary => summary.ArchetypeId, StringComparer.OrdinalIgnoreCase)
                .Select(SummarizeArchetype)
                .OrderBy(summary => summary.EnemyWeakness)
                .ThenBy(summary => summary.PreferredDamageType)
                .ThenBy(summary => summary.ArchetypeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private MaxDamageReferenceTeamSummary SummarizeTeam(MaxDamageReferenceTeam team)
        {
            var weakness = ParseElement(team.Element, team.IsNonElementBattle);
            var preferredDamageType = ParseDamageType(team.StrategyTags);
            var characterSummaries = team.Characters
                .Select(character => SummarizeCharacter(character, weakness))
                .ToList();
            var orderedRoleNames = characterSummaries
                .Select(summary => summary.Role.ToString())
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var profileNotes = new List<string>();
            if (characterSummaries.Any(summary => summary.Role is CharacterRole.Healer or CharacterRole.Support) && characterSummaries.Any(summary => summary.Role is CharacterRole.Healer or CharacterRole.Support && summary.HasDebuffSeedMateria))
            {
                profileNotes.Add("Support/healer slot carries debuff seed materia.");
            }

            if (characterSummaries.Any(summary => summary.Role == CharacterRole.DPS && summary.HasLikelyTripleElementLoadout))
            {
                profileNotes.Add("At least one DPS runs a triple element materia loadout.");
            }

            if (characterSummaries.Any(summary => summary.HasStatStickMateria))
            {
                profileNotes.Add("Includes likely stat-stick materia.");
            }

            if (characterSummaries.Any(summary => summary.HasStatDebuffTierIncreaseSource))
            {
                profileNotes.Add("Includes a stat debuff tier increase source.");
            }

            return new MaxDamageReferenceTeamSummary
            {
                TeamId = team.Id,
                ArchetypeId = team.ArchetypeId,
                EnemyWeakness = weakness,
                PreferredDamageType = preferredDamageType,
                IsNonElementBattle = team.IsNonElementBattle,
                Rank = team.Rank,
                TeamMemoria = team.TeamMemoria?.Trim() ?? string.Empty,
                CharacterNames = characterSummaries.Select(summary => summary.CharacterName).ToList(),
                TeamRoleKey = string.Join("/", orderedRoleNames),
                Characters = characterSummaries,
                HasAnyDebuffSeedSetup = characterSummaries.Any(summary => summary.HasDebuffSeedMateria),
                HasSupportOrHealerDebuffSeedSetup = characterSummaries.Any(summary => summary.Role is CharacterRole.Healer or CharacterRole.Support && summary.HasDebuffSeedMateria),
                HasAnyTripleElementDpsLoadout = characterSummaries.Any(summary => summary.Role == CharacterRole.DPS && summary.HasLikelyTripleElementLoadout),
                HasAnyStatStickMateria = characterSummaries.Any(summary => summary.HasStatStickMateria),
                HasStatDebuffTierIncreaseSource = characterSummaries.Any(summary => summary.HasStatDebuffTierIncreaseSource),
                ProfileNotes = profileNotes
            };
        }

        private MaxDamageReferenceCharacterSummary SummarizeCharacter(MaxDamageReferenceCharacter character, Element weakness)
        {
            var normalizedCharacterName = NormalizeCharacterName(character.CharacterName);
            var role = CharacterRoleRegistry.GetRoleOrDefault(normalizedCharacterName);
            var materiaRoles = character.Materia
                .Where(materia => !string.IsNullOrWhiteSpace(materia))
                .Select(materia => DetectMateriaRole(materia, weakness))
                .ToList();
            var elementDamageMateriaCount = materiaRoles.Count(role => role is MaxDamageReferenceMateriaRole.ElementDamagePhysical or MaxDamageReferenceMateriaRole.ElementDamageMagical);
            var debuffSeedMateriaCount = materiaRoles.Count(role => role is MaxDamageReferenceMateriaRole.ElementDebuffSeed or MaxDamageReferenceMateriaRole.PdefDebuffSeed or MaxDamageReferenceMateriaRole.MdefDebuffSeed);
            var statStickCandidateCount = materiaRoles.Count(role => role == MaxDamageReferenceMateriaRole.StatStickCandidate);
            var hasStatDebuffTierIncreaseSource = DetectStatDebuffTierIncreaseSource(character);
            var hasDebuffSeedMateria = debuffSeedMateriaCount > 0;
            var hasLikelyTripleElementLoadout = role == CharacterRole.DPS && elementDamageMateriaCount >= 3;
            var hasStatStickMateria = statStickCandidateCount > 0;
            var materiaProfileLabel = BuildMateriaProfileLabel(role, hasDebuffSeedMateria, hasLikelyTripleElementLoadout, hasStatStickMateria, hasStatDebuffTierIncreaseSource, elementDamageMateriaCount);

            return new MaxDamageReferenceCharacterSummary
            {
                CharacterName = normalizedCharacterName,
                Role = role,
                Materia = character.Materia.Where(materia => !string.IsNullOrWhiteSpace(materia)).ToList(),
                MateriaRoles = materiaRoles,
                ElementDamageMateriaCount = elementDamageMateriaCount,
                DebuffSeedMateriaCount = debuffSeedMateriaCount,
                StatStickCandidateCount = statStickCandidateCount,
                HasStatDebuffTierIncreaseSource = hasStatDebuffTierIncreaseSource,
                HasDebuffSeedMateria = hasDebuffSeedMateria,
                HasLikelyTripleElementLoadout = hasLikelyTripleElementLoadout,
                HasStatStickMateria = hasStatStickMateria,
                MateriaProfileLabel = materiaProfileLabel
            };
        }

        private MaxDamageReferenceArchetypeSummary SummarizeArchetype(IGrouping<string, MaxDamageReferenceTeamSummary> group)
        {
            var teams = group.ToList();
            var first = teams[0];
            var commonMemoria = teams
                .Select(team => team.TeamMemoria)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(grouping => grouping.Count())
                .ThenBy(grouping => grouping.Key, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(grouping => grouping.Key)
                .ToList();
            var characterNames = teams
                .SelectMany(team => team.CharacterNames)
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(grouping => grouping.Count())
                .ThenBy(grouping => grouping.Key, StringComparer.OrdinalIgnoreCase)
                .Select(grouping => grouping.Key)
                .ToList();
            var notes = new List<string>();
            if (teams.Any(team => team.HasSupportOrHealerDebuffSeedSetup))
            {
                notes.Add("Often uses support/healer debuff seed setup.");
            }

            if (teams.Any(team => team.HasAnyTripleElementDpsLoadout))
            {
                notes.Add("Often uses triple element materia on DPS slots.");
            }

            if (teams.Any(team => team.HasAnyStatStickMateria))
            {
                notes.Add("Sometimes carries likely stat-stick materia.");
            }

            if (teams.Any(team => team.HasStatDebuffTierIncreaseSource))
            {
                notes.Add("Often pairs seed debuffs with stat debuff tier increase gear.");
            }

            return new MaxDamageReferenceArchetypeSummary
            {
                ArchetypeId = group.Key,
                EnemyWeakness = first.EnemyWeakness,
                PreferredDamageType = first.PreferredDamageType,
                IsNonElementBattle = first.IsNonElementBattle,
                TeamCount = teams.Count,
                CharacterNames = characterNames,
                CommonMemoria = commonMemoria,
                TeamsWithAnyDebuffSeedSetup = teams.Count(team => team.HasAnyDebuffSeedSetup),
                TeamsWithSupportOrHealerDebuffSeedSetup = teams.Count(team => team.HasSupportOrHealerDebuffSeedSetup),
                TeamsWithAnyTripleElementDpsLoadout = teams.Count(team => team.HasAnyTripleElementDpsLoadout),
                TeamsWithAnyStatStickMateria = teams.Count(team => team.HasAnyStatStickMateria),
                TeamsWithStatDebuffTierIncreaseSource = teams.Count(team => team.HasStatDebuffTierIncreaseSource),
                Notes = notes
            };
        }

        private MaxDamageReferenceMatchResult BuildMatchResult(MaxDamageReferenceTeamSummary summary, Element weakness, DamageType preferredDamageType, IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characters, IEnumerable<string> providedEffectKeys)
        {
            var score = 0d;
            var matchingSignals = new List<string>();
            var missingSignals = new List<string>();
            var characterNames = characters.Select(character => NormalizeCharacterName(character.CharacterName)).ToList();
            var liveCharacterSet = new HashSet<string>(characterNames, StringComparer.OrdinalIgnoreCase);
            var liveRoleKey = string.Join("/", characters.Select(character => character.Role.ToString()).OrderBy(role => role, StringComparer.OrdinalIgnoreCase));
            var normalizedProvidedEffects = new HashSet<string>(providedEffectKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            if (summary.EnemyWeakness == weakness)
            {
                score += 60;
                matchingSignals.Add($"Battle element match: {weakness}");
            }
            else
            {
                missingSignals.Add($"Reference battle element differs ({summary.EnemyWeakness})");
            }

            if (summary.PreferredDamageType == preferredDamageType)
            {
                score += 35;
                matchingSignals.Add($"Damage type match: {preferredDamageType}");
            }
            else
            {
                missingSignals.Add($"Reference damage type differs ({summary.PreferredDamageType})");
            }

            if (string.Equals(summary.TeamRoleKey, liveRoleKey, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                matchingSignals.Add($"Role shell match: {liveRoleKey}");
            }

            var overlappingCharacters = summary.CharacterNames
                .Where(liveCharacterSet.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (overlappingCharacters.Count > 0)
            {
                score += overlappingCharacters.Count * 25;
                matchingSignals.Add($"Character overlap: {string.Join(", ", overlappingCharacters)}");
            }
            else
            {
                missingSignals.Add("No character overlap with this reference team.");
            }

            if (summary.HasStatDebuffTierIncreaseSource)
            {
                if (normalizedProvidedEffects.Contains("stat_debuff_tier_increase"))
                {
                    score += 20;
                    matchingSignals.Add("Both builds include stat debuff tier increase utility.");
                }
                else
                {
                    missingSignals.Add("Reference uses stat debuff tier increase utility.");
                }
            }

            if (summary.HasSupportOrHealerDebuffSeedSetup)
            {
                if (normalizedProvidedEffects.Overlaps(new[] { "pdef_down", "mdef_down", "elemental_resistance_down" }))
                {
                    score += 15;
                    matchingSignals.Add("Both builds show debuff setup coverage.");
                }
                else if (CanAssumeStandardSynthDebuffSeedSetup(characters, weakness))
                {
                    score += 8;
                    matchingSignals.Add("Reference debuff seed setup can be approximated with standard synth materia.");
                }
                else
                {
                    missingSignals.Add("Reference uses support/healer debuff seed setup.");
                }
            }

            if (summary.HasAnyTripleElementDpsLoadout)
            {
                if (CanAssumeStandardSynthElementMateria(characters, weakness))
                {
                    score += 10;
                    matchingSignals.Add("Reference elemental DPS materia pattern can be approximated with standard synth materia.");
                }
                else
                {
                    missingSignals.Add("Reference uses triple element DPS materia loadout.");
                }
            }

            return new MaxDamageReferenceMatchResult
            {
                ArchetypeId = summary.ArchetypeId,
                Score = Math.Round(score, 2),
                MatchingSignals = matchingSignals,
                MissingSignals = missingSignals,
                ReferenceProfileNotes = summary.ProfileNotes.ToList(),
                ReferenceCharacters = summary.CharacterNames.ToList()
            };
        }

        private static bool CanAssumeStandardSynthDebuffSeedSetup(IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characters, Element weakness)
        {
            var hasSupportOrHealer = characters.Any(character => character.Role is CharacterRole.Support or CharacterRole.Healer);
            if (!hasSupportOrHealer)
            {
                return false;
            }

            return weakness != Element.None || characters.Any(character => character.Role is CharacterRole.Support or CharacterRole.Healer);
        }

        private static bool CanAssumeStandardSynthElementMateria(IReadOnlyList<PlayerPowerAnalyzerV2CharacterBuild> characters, Element weakness)
        {
            return weakness != Element.None
                && characters.Any(character => character.Role == CharacterRole.DPS);
        }

        private bool DetectStatDebuffTierIncreaseSource(MaxDamageReferenceCharacter character)
        {
            foreach (var itemName in EnumerateEquipmentNames(character))
            {
                var normalizedItemName = NormalizeItemName(itemName);
                var item = _weaponSearchDataService.TryGetWeaponSearchItemByName(normalizedItemName);
                if (item == null)
                {
                    continue;
                }

                var blob = string.Join(" | ", item.EffectTags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Append(item.AbilityText ?? string.Empty));
                if (blob.Contains("Applied Stats Debuff Tier Increased", StringComparison.OrdinalIgnoreCase)
                    || blob.Contains("Stats Debuff Tier Increased", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumerateEquipmentNames(MaxDamageReferenceCharacter character)
        {
            if (!string.IsNullOrWhiteSpace(character.MainWeapon))
            {
                yield return character.MainWeapon;
            }

            if (!string.IsNullOrWhiteSpace(character.OffHandWeapon))
            {
                yield return character.OffHandWeapon;
            }

            if (!string.IsNullOrWhiteSpace(character.UltimateWeapon))
            {
                yield return character.UltimateWeapon;
            }

            if (!string.IsNullOrWhiteSpace(character.Costume))
            {
                yield return character.Costume;
            }

            foreach (var costume in character.SubCostumes)
            {
                if (!string.IsNullOrWhiteSpace(costume))
                {
                    yield return costume;
                }
            }
        }

        private static string BuildMateriaProfileLabel(CharacterRole role, bool hasDebuffSeedMateria, bool hasLikelyTripleElementLoadout, bool hasStatStickMateria, bool hasStatDebuffTierIncreaseSource, int elementDamageMateriaCount)
        {
            if (role == CharacterRole.DPS && hasLikelyTripleElementLoadout)
            {
                return "Triple element DPS loadout";
            }

            if ((role is CharacterRole.Healer or CharacterRole.Support) && hasDebuffSeedMateria && hasStatDebuffTierIncreaseSource)
            {
                return "Support debuff seed plus tier increase setup";
            }

            if ((role is CharacterRole.Healer or CharacterRole.Support) && hasDebuffSeedMateria)
            {
                return "Support debuff seed setup";
            }

            if (hasStatStickMateria)
            {
                return "Includes likely stat-stick materia";
            }

            if (elementDamageMateriaCount > 0)
            {
                return "Element damage materia loadout";
            }

            return "Mixed utility materia loadout";
        }

        private static MaxDamageReferenceMateriaRole DetectMateriaRole(string materiaName, Element weakness)
        {
            var normalized = materiaName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return MaxDamageReferenceMateriaRole.Unknown;
            }

            if (normalized.Equals("Mana Breach", StringComparison.OrdinalIgnoreCase))
            {
                return MaxDamageReferenceMateriaRole.MdefDebuffSeed;
            }

            if (normalized.Equals("Breach", StringComparison.OrdinalIgnoreCase))
            {
                return MaxDamageReferenceMateriaRole.PdefDebuffSeed;
            }

            if (normalized.Contains("Breach", StringComparison.OrdinalIgnoreCase))
            {
                return MaxDamageReferenceMateriaRole.ElementDebuffSeed;
            }

            if (normalized.Equals("Cure", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Cura", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Curaga", StringComparison.OrdinalIgnoreCase))
            {
                return MaxDamageReferenceMateriaRole.Healing;
            }

            if (normalized.Equals("Bravery", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Faith", StringComparison.OrdinalIgnoreCase))
            {
                return MaxDamageReferenceMateriaRole.SupportBuff;
            }

            if (normalized.StartsWith("Ruin", StringComparison.OrdinalIgnoreCase))
            {
                return MaxDamageReferenceMateriaRole.StatStickCandidate;
            }

            if (IsElementMateria(normalized, weakness))
            {
                return normalized.Contains("Blow", StringComparison.OrdinalIgnoreCase)
                    ? MaxDamageReferenceMateriaRole.ElementDamagePhysical
                    : MaxDamageReferenceMateriaRole.ElementDamageMagical;
            }

            return MaxDamageReferenceMateriaRole.Utility;
        }

        private static bool IsElementMateria(string materiaName, Element weakness)
        {
            var elementPrefixes = new[]
            {
                "Fire", "Fira", "Firaga",
                "Ice", "Blizzard", "Blizzara", "Blizzaga",
                "Water", "Watera", "Waterga",
                "Thunder", "Thundara", "Thundaga", "Lightning",
                "Aero", "Aerora", "Aeroga", "Wind",
                "Quake", "Quakera", "Quakega", "Earth"
            };

            if (elementPrefixes.Any(prefix => materiaName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (weakness != Element.None)
            {
                return materiaName.Contains(weakness.ToString(), StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static Element ParseElement(string? value, bool isNonElementBattle)
        {
            if (isNonElementBattle)
            {
                return Element.None;
            }

            return Enum.TryParse<Element>(value ?? string.Empty, ignoreCase: true, out var element)
                ? element
                : Element.None;
        }

        private static DamageType ParseDamageType(IEnumerable<string> strategyTags)
        {
            foreach (var tag in strategyTags ?? Array.Empty<string>())
            {
                if (tag.Equals("physical", StringComparison.OrdinalIgnoreCase))
                {
                    return DamageType.Physical;
                }

                if (tag.Equals("magic", StringComparison.OrdinalIgnoreCase) || tag.Equals("magical", StringComparison.OrdinalIgnoreCase))
                {
                    return DamageType.Magical;
                }
            }

            return DamageType.Any;
        }

        private static string NormalizeCharacterName(string? name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            return CharacterAliases.TryGetValue(trimmed, out var alias) ? alias : trimmed;
        }

        private static string NormalizeItemName(string? name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            return ItemAliases.TryGetValue(trimmed, out var alias) ? alias : trimmed;
        }
    }
}
