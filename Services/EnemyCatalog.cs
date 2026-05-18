using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFVIIEverCrisisAnalyzer.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class EnemyCatalog
    {
        private const string BattleModeAll = "all";
        private const string BattleModeSolo = "solo";
        private const string BattleModeCoop = "coop";

        private readonly ILogger<EnemyCatalog> _logger;
        private readonly Dictionary<int, EnemyRecord> _enemies = new();
        private readonly Dictionary<int, List<int>> _levelsByEnemyId = new();
        private readonly Dictionary<int, List<EnemyLevelParameterBaseRaw>> _levelParamsByGroup = new();
        private readonly Dictionary<int, EnemyLevelConstantRaw> _levelConstantsByGroup = new();
        private readonly Dictionary<int, IReadOnlyList<string>> _speciesByGroup = new();
        private readonly Dictionary<long, List<ResistElementRaw>> _elementResistsById = new();
        private readonly Dictionary<int, List<ResistStatusConditionRaw>> _statusResistsById = new();
        private readonly Dictionary<long, List<ResistBuffDebuffRaw>> _buffResistsById = new();
        private readonly Dictionary<long, string> _eventBattleLocalization = new();
        private readonly LocalizationStore _localizationStore;
        private readonly ConcurrentDictionary<(int enemyId, int level), EnemyDetailView> _detailCache = new();
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static readonly Regex MarkupRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex BreakTagRegex = new(@"<\s*br\s*/?\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ParagraphCloseTagRegex = new(@"<\s*/\s*p\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] ElementNamesByType =
        {
            "Unknown",
            "Non-Elemental",
            "Fire",
            "Ice",
            "Lightning",
            "Earth",
            "Water",
            "Wind",
            "Holy",
            "Dark"
        };

        private static readonly Dictionary<int, string> StatusEffectTypes = new()
        {
            { 1, "Poison" },
            { 2, "Sedate" },
            { 3, "Silence" },
            { 4, "Darkness" },
            { 5, "Stun" },
            { 6, "Fatigue" },
            { 7, "Fog" },
            { 8, "Sleep" },
            { 9, "Confusion" },
            { 10, "Slow" },
            { 11, "Doom" },
            { 12, "Dread" },
            { 13, "Venom" },
            { 14, "Pain" },
            { 15, "Agony" },
            { 16, "Enfeeble" },
            { 17, "Stop" },
            { 18, "Fire Weakness" },
            { 19, "Ice Weakness" },
            { 20, "Lightning Weakness" },
            { 21, "Earth Weakness" },
            { 22, "Water Weakness" },
            { 23, "Wind Weakness" },
            { 26, "Single-Tgt. Phys. Dmg. Rcvd. Up" },
            { 27, "Single-Tgt. Phys. Dmg. Rcvd. Up" },
            { 28, "Single-Tgt. Mag. Dmg. Rcvd. Up" },
            { 29, "All-Tgt. Phys. Dmg. Rcvd. Up" },
            { 30, "All-Tgt. Mag. Dmg. Rcvd. Up" }
        };

        private static readonly Dictionary<int, string> BuffDebuffTypes = new()
        {
            { 5, "PATK Down" },
            { 6, "PDEF Down" },
            { 7, "MATK Down" },
            { 8, "MDEF Down" },
            { 24, "PATK Down" },
            { 26, "MATK Down" }
        };

        public EnemyCatalog(ILogger<EnemyCatalog> logger, IWebHostEnvironment environment)
        {
            _logger = logger;

            var contentRoot = environment.ContentRootPath;
            var basePath = Path.Combine(contentRoot, "external", "UnknownX7", "FF7EC-Data");
            var localizationPath = Path.Combine(basePath, "Localization", "en.json");
            _localizationStore = LoadLocalization(localizationPath);

            LoadEventBattleLocalization(Path.Combine(basePath, "Localization", "en", "m_event_solo_battle.json"));
            LoadEventBattleLocalization(Path.Combine(basePath, "Localization", "en", "m_event_multi_battle.json"));

            var masterPath = Path.Combine(basePath, "MasterData", "gl");
            if (!Directory.Exists(masterPath))
            {
                _logger.LogWarning("Master data directory not found at {Path}", masterPath);
                return;
            }

            try
            {
                LoadEnemyData(masterPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load enemy catalog data");
            }
        }

        public IReadOnlyList<EnemySearchResult> SearchEnemies(string query, string? battleMode = null)
        {
            if (string.IsNullOrWhiteSpace(query) || _enemies.Count == 0)
            {
                return Array.Empty<EnemySearchResult>();
            }

            query = query.Trim();
            var normalizedBattleMode = NormalizeBattleMode(battleMode);
            var results = new List<EnemySearchResult>();

            foreach (var record in _enemies.Values)
            {
                var stageNamesForMode = GetStageNamesForMode(record, normalizedBattleMode);
                if (!string.Equals(normalizedBattleMode, BattleModeAll, StringComparison.OrdinalIgnoreCase)
                    && stageNamesForMode.Count == 0)
                {
                    continue;
                }

                var matchesName = record.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchingStageNames = stageNamesForMode.Count == 0
                    ? new List<string>()
                    : stageNamesForMode
                        .Where(stage => stage.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                var matchesStage = matchingStageNames.Count > 0;

                if (!matchesName && !matchesStage)
                {
                    continue;
                }

                if (!_levelsByEnemyId.TryGetValue(record.Id, out var levels) || levels.Count == 0)
                {
                    continue;
                }

                if (matchesName)
                {
                    AddEnemyRows(record, levels, results);
                }

                if (matchesStage)
                {
                    AddStageRows(record, levels, matchingStageNames, results);
                }
            }

            return results;
        }

        public IReadOnlyList<string> GetSearchSuggestions(string? battleMode = null)
        {
            if (_enemies.Count == 0)
            {
                return Array.Empty<string>();
            }

            var normalizedBattleMode = NormalizeBattleMode(battleMode);

            return _enemies.Values
                .SelectMany(record => new[] { record.Name }.Concat(GetStageNamesForMode(record, normalizedBattleMode)))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToList();
        }

        private static IReadOnlyList<string> GetStageNamesForMode(EnemyRecord record, string normalizedBattleMode)
        {
            return normalizedBattleMode switch
            {
                BattleModeSolo => record.SoloStageNames,
                BattleModeCoop => record.CoopStageNames,
                _ => record.StageNames
            };
        }

        private static string NormalizeBattleMode(string? battleMode)
        {
            if (string.Equals(battleMode, BattleModeSolo, StringComparison.OrdinalIgnoreCase))
            {
                return BattleModeSolo;
            }

            if (string.Equals(battleMode, BattleModeCoop, StringComparison.OrdinalIgnoreCase))
            {
                return BattleModeCoop;
            }

            return BattleModeAll;
        }

        private static void AddEnemyRows(EnemyRecord record, IReadOnlyList<int> levels, List<EnemySearchResult> results)
        {
            foreach (var level in levels.OrderBy(l => l))
            {
                results.Add(new EnemySearchResult
                {
                    EnemyId = record.Id,
                    Level = level,
                    Name = record.Name,
                    SpeciesSummary = record.SpeciesSummary,
                    StageNames = record.StageNames,
                    SoloStageNames = record.SoloStageNames,
                    CoopStageNames = record.CoopStageNames,
                    StageSummary = record.StageSummary,
                    DisplayName = record.Name,
                    DisplayLevelText = level.ToString(),
                    IsStageResult = false,
                    StageName = string.Empty
                });
            }
        }

        private static void AddStageRows(EnemyRecord record, IReadOnlyList<int> levels, IReadOnlyList<string> stageNames, List<EnemySearchResult> results)
        {
            if (stageNames == null || stageNames.Count == 0)
            {
                return;
            }

            var fallbackLevel = levels.OrderBy(l => l).FirstOrDefault();
            if (fallbackLevel == 0)
            {
                fallbackLevel = 1;
            }

            foreach (var stageName in stageNames)
            {
                results.Add(new EnemySearchResult
                {
                    EnemyId = record.Id,
                    Level = fallbackLevel,
                    Name = record.Name,
                    SpeciesSummary = record.SpeciesSummary,
                    StageNames = record.StageNames,
                    SoloStageNames = record.SoloStageNames,
                    CoopStageNames = record.CoopStageNames,
                    StageSummary = record.StageSummary,
                    DisplayName = record.Name,
                    DisplayLevelText = fallbackLevel.ToString(),
                    IsStageResult = true,
                    StageName = stageName
                });
            }
        }

        public EnemyDetailView? GetEnemyDetails(int enemyId, int level)
        {
            if (_enemies.Count == 0)
            {
                return null;
            }

            var cacheKey = (enemyId, level);

            if (!_enemies.TryGetValue(enemyId, out var enemy))
            {
                return null;
            }

            if (!_levelsByEnemyId.TryGetValue(enemyId, out var levels) || levels.Count == 0)
            {
                return null;
            }

            if (!levels.Contains(level))
            {
                level = levels.OrderBy(l => Math.Abs(l - level)).First();
            }

            var baseStats = FindLevelStats(enemy.LevelParameterGroupId, level);
            if (baseStats == null)
            {
                return null;
            }

            var elementResistances = BuildElementResistances(enemy.ResistElementId);
            if (elementResistances.Count == 0)
            {
                elementResistances = BuildElementResistances(enemy.ResistStatusConditionId);
                if (elementResistances.Count == 0)
                {
                    elementResistances = BuildElementResistances(enemy.ResistBuffDebuffId);
                }
            }

            var statusImmunities = BuildStatusImmunities(enemy.ResistStatusConditionId);
            if (statusImmunities.Count == 0)
            {
                statusImmunities = BuildStatusImmunities(enemy.ResistBuffDebuffId);
            }

            var buffImmunities = BuildBuffImmunities(enemy.ResistBuffDebuffId);
            if (buffImmunities.Count == 0)
            {
                buffImmunities = BuildBuffImmunities(enemy.ResistStatusConditionId);
            }

            var detail = new EnemyDetailView
            {
                EnemyId = enemyId,
                Level = level,
                Name = enemy.Name,
                Hp = ComputeHp(baseStats.BaseHp, enemy.HpCoefficient, baseStats.BaseLbHp, enemy.LbHpCoefficient),
                PhysicalAttack = (int)ComputeStat(baseStats.BasePhysicalAttack, enemy.PhysicalAttackCoefficient),
                MagicalAttack = (int)ComputeStat(baseStats.BaseMagicalAttack, enemy.MagicalAttackCoefficient),
                PhysicalDefense = (int)ComputeStat(baseStats.BasePhysicalDefense, enemy.PhysicalDefenseCoefficient),
                MagicalDefense = (int)ComputeStat(baseStats.BaseMagicalDefense, enemy.MagicalDefenseCoefficient),
                Species = enemy.Species,
                ElementResistances = elementResistances,
                StatusImmunities = statusImmunities,
                BuffDebuffImmunities = buffImmunities,
                Description = BuildDescription(enemy.LevelConstantGroupId),
                SkillSummaries = Array.Empty<string>()
            };

            _detailCache[cacheKey] = detail;
            return detail;
        }

        private void LoadEnemyData(string masterPath)
        {
            var enemies = LoadList<EnemyRaw>(Path.Combine(masterPath, "Enemy.json"));
            var battleEnemies = LoadList<BattleEnemyRaw>(Path.Combine(masterPath, "BattleEnemy.json"));
            var levelParams = LoadList<EnemyLevelParameterBaseRaw>(Path.Combine(masterPath, "EnemyLevelParameterBase.json"));
            var levelConstants = LoadList<EnemyLevelConstantRaw>(Path.Combine(masterPath, "EnemyLevelConstant.json"));
            var resistElements = LoadList<ResistElementRaw>(Path.Combine(masterPath, "ResistElement.json"));
            var resistStatus = LoadList<ResistStatusConditionRaw>(Path.Combine(masterPath, "ResistStatusCondition.json"));
            var resistBuffs = LoadList<ResistBuffDebuffRaw>(Path.Combine(masterPath, "ResistBuffDebuff.json"));
            var species = LoadList<SpeciesRaw>(Path.Combine(masterPath, "Species.json"));
            var speciesRels = LoadList<SpeciesGroupRelRaw>(Path.Combine(masterPath, "SpeciesGroupRel.json"));
            var soloAreaBattles = LoadList<SoloAreaBattleRaw>(Path.Combine(masterPath, "SoloAreaBattle.json"));
            var eventSoloBattles = LoadList<EventSoloBattleRaw>(Path.Combine(masterPath, "EventSoloBattle.json"));
            var eventMultiBattles = LoadList<EventMultiBattleRaw>(Path.Combine(masterPath, "EventMultiBattle.json"));
            var battles = LoadList<BattleRaw>(Path.Combine(masterPath, "Battle.json"));
            var battleWaves = LoadList<BattleWaveRaw>(Path.Combine(masterPath, "BattleWave.json"));

            foreach (var group in levelParams.GroupBy(p => p.EnemyLevelParameterBaseGroupId))
            {
                _levelParamsByGroup[group.Key] = group.OrderBy(p => p.Level).ToList();
            }

            foreach (var constant in levelConstants)
            {
                _levelConstantsByGroup[constant.EnemyLevelConstantGroupId] = constant;
            }

            foreach (var group in resistElements.GroupBy(r => r.Id))
            {
                _elementResistsById[group.Key] = group.ToList();
            }

            foreach (var group in resistStatus.GroupBy(r => r.Id))
            {
                _statusResistsById[group.Key] = group.ToList();
            }

            foreach (var group in resistBuffs.GroupBy(r => r.Id))
            {
                _buffResistsById[group.Key] = group.ToList();
            }

            var speciesNames = species.ToDictionary(s => s.Id, s => _localizationStore.Get(s.NameLanguageId));
            foreach (var group in speciesRels.GroupBy(r => r.SpeciesGroupId))
            {
                var names = group.Select(rel => speciesNames.TryGetValue(rel.SpeciesId, out var name) ? name : string.Empty)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _speciesByGroup[group.Key] = names;
            }

            var enemyStageNames = BuildStageNameLookup(soloAreaBattles, eventSoloBattles, eventMultiBattles, battles, battleWaves, battleEnemies);

            foreach (var enemy in enemies)
            {
                var stageNames = enemyStageNames.TryGetValue(enemy.Id, out var stageList)
                    ? stageList
                    : StageNamesByMode.Empty;
                var speciesNamesForEnemy = _speciesByGroup.TryGetValue(enemy.SpeciesGroupId, out var speciesList)
                    ? speciesList
                    : Array.Empty<string>();
                var record = new EnemyRecord
                {
                    Id = enemy.Id,
                    Name = _localizationStore.Get(enemy.NameLanguageId),
                    LevelParameterGroupId = enemy.EnemyLevelParameterBaseGroupId,
                    LevelConstantGroupId = enemy.EnemyLevelConstantGroupId,
                    HpCoefficient = enemy.HpCoefficient,
                    LbHpCoefficient = enemy.LbHpCoefficient,
                    PhysicalAttackCoefficient = enemy.PhysicalAttackCoefficient,
                    MagicalAttackCoefficient = enemy.MagicalAttackCoefficient,
                    PhysicalDefenseCoefficient = enemy.PhysicalDefenseCoefficient,
                    MagicalDefenseCoefficient = enemy.MagicalDefenseCoefficient,
                    ResistElementId = enemy.ResistElementId,
                    ResistStatusConditionId = enemy.ResistStatusConditionId,
                    ResistBuffDebuffId = enemy.ResistBuffDebuffId,
                    Species = speciesNamesForEnemy,
                    StageNames = stageNames.All,
                    SoloStageNames = stageNames.Solo,
                    CoopStageNames = stageNames.Coop,
                    StageSummary = BuildStageSummary(stageNames.All)
                };

                record.SpeciesSummary = record.Species.Count > 0
                    ? string.Join(", ", record.Species)
                    : "Unknown";

                if (!string.IsNullOrWhiteSpace(record.Name))
                {
                    _enemies[record.Id] = record;
                }
            }

            foreach (var group in battleEnemies.GroupBy(b => b.EnemyId))
            {
                _levelsByEnemyId[group.Key] = group.Select(b => b.Level)
                    .Distinct()
                    .OrderBy(l => l)
                    .ToList();
            }

        }

        private Dictionary<int, StageNamesByMode> BuildStageNameLookup(
            IReadOnlyList<SoloAreaBattleRaw> soloAreaBattles,
            IReadOnlyList<EventSoloBattleRaw> eventSoloBattles,
            IReadOnlyList<EventMultiBattleRaw> eventMultiBattles,
            IReadOnlyList<BattleRaw> battles,
            IReadOnlyList<BattleWaveRaw> battleWaves,
            IReadOnlyList<BattleEnemyRaw> battleEnemies)
        {
            if (battles.Count == 0 || battleWaves.Count == 0 || battleEnemies.Count == 0)
            {
                return new();
            }

            var stageNamesByBattleId = new Dictionary<long, StageNameAccumulator>();

            AppendStageNamesByBattleId(
                stageNamesByBattleId,
                soloAreaBattles.Select(s => (s.BattleId, s.NameLanguageId, BattleModeSolo)));

            AppendStageNamesByBattleId(
                stageNamesByBattleId,
                eventSoloBattles.Select(s => (s.BattleId, s.NameLanguageId, BattleModeSolo)));

            AppendStageNamesByBattleId(
                stageNamesByBattleId,
                eventMultiBattles.Select(s => (s.BattleId, s.NameLanguageId, BattleModeCoop)));

            if (stageNamesByBattleId.Count == 0)
            {
                return new();
            }

            var waveGroupByBattleId = battles.ToDictionary(b => b.Id, b => b.WaveGroupId);
            var enemyGroupIdsByWaveGroupId = battleWaves
                .GroupBy(w => w.WaveGroupId)
                .ToDictionary(g => g.Key, g => g.Select(w => w.EnemyGroupId).Distinct().ToList());

            var enemyIdsByGroupId = battleEnemies
                .GroupBy(b => b.EnemyGroupId)
                .ToDictionary(g => g.Key, g => g.Select(b => b.EnemyId).Distinct().ToList());

            var stageNamesByEnemy = new Dictionary<int, StageNameAccumulator>();

            foreach (var stage in stageNamesByBattleId)
            {
                if (!waveGroupByBattleId.TryGetValue(stage.Key, out var waveGroupId))
                {
                    continue;
                }

                if (!enemyGroupIdsByWaveGroupId.TryGetValue(waveGroupId, out var enemyGroupIds))
                {
                    continue;
                }

                var stageNameList = stage.Value;
                foreach (var enemyGroupId in enemyGroupIds)
                {
                    if (!enemyIdsByGroupId.TryGetValue(enemyGroupId, out var enemyIds))
                    {
                        continue;
                    }

                    foreach (var enemyId in enemyIds)
                    {
                        if (!stageNamesByEnemy.TryGetValue(enemyId, out var list))
                        {
                            list = new StageNameAccumulator();
                            stageNamesByEnemy[enemyId] = list;
                        }

                        foreach (var stageName in stageNameList.All)
                        {
                            if (!string.IsNullOrWhiteSpace(stageName))
                            {
                                list.All.Add(stageName);
                            }
                        }

                        foreach (var stageName in stageNameList.Solo)
                        {
                            if (!string.IsNullOrWhiteSpace(stageName))
                            {
                                list.Solo.Add(stageName);
                            }
                        }

                        foreach (var stageName in stageNameList.Coop)
                        {
                            if (!string.IsNullOrWhiteSpace(stageName))
                            {
                                list.Coop.Add(stageName);
                            }
                        }
                    }
                }
            }

            return stageNamesByEnemy.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToStageNamesByMode());
        }

        private void AppendStageNamesByBattleId(
            Dictionary<long, StageNameAccumulator> stageNamesByBattleId,
            IEnumerable<(long BattleId, long NameLanguageId, string Mode)> mappings)
        {
            foreach (var (battleId, nameLanguageId, mode) in mappings)
            {
                if (battleId <= 0 || nameLanguageId <= 0)
                {
                    continue;
                }

                var name = ResolveStageName(nameLanguageId);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!stageNamesByBattleId.TryGetValue(battleId, out var names))
                {
                    names = new StageNameAccumulator();
                    stageNamesByBattleId[battleId] = names;
                }

                names.All.Add(name);

                if (string.Equals(mode, BattleModeCoop, StringComparison.OrdinalIgnoreCase))
                {
                    names.Coop.Add(name);
                }
                else
                {
                    names.Solo.Add(name);
                }
            }
        }

        private string ResolveStageName(long languageId)
        {
            var localized = _localizationStore.Get(languageId);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            return _eventBattleLocalization.TryGetValue(languageId, out var eventLocalized)
                ? eventLocalized
                : string.Empty;
        }

        private static string BuildStageSummary(IReadOnlyList<string> stageNames)
        {
            if (stageNames == null || stageNames.Count == 0)
            {
                return string.Empty;
            }

            const int previewCount = 3;
            var preview = stageNames.Take(previewCount).ToList();
            var summary = string.Join(", ", preview);
            if (stageNames.Count > previewCount)
            {
                summary += $" (+{stageNames.Count - previewCount} more)";
            }

            return summary;
        }

        private EnemyLevelParameterBaseRaw? FindLevelStats(int groupId, int level)
        {
            if (!_levelParamsByGroup.TryGetValue(groupId, out var list) || list.Count == 0)
            {
                return null;
            }

            var match = list.FirstOrDefault(p => p.Level == level);
            if (match != null)
            {
                return match;
            }

            return list.OrderBy(p => Math.Abs(p.Level - level)).First();
        }

        private IReadOnlyList<ResistanceEntry> BuildElementResistances(long resistId)
        {
            if (!_elementResistsById.TryGetValue(resistId, out var entries))
            {
                return Array.Empty<ResistanceEntry>();
            }

            return entries.Where(e => e.DamageCoefficient != 0)
                .Select(e => new ResistanceEntry
                {
                    Type = ResolveElementName(e.ElementType),
                    Value = $"{e.DamageCoefficient / 10.0:0.#}%"
                })
                .ToList();
        }

        private static string ResolveElementName(int elementType)
        {
            if (elementType >= 0 && elementType < ElementNamesByType.Length)
            {
                return ElementNamesByType[elementType];
            }

            return $"Element {elementType}";
        }

        private IReadOnlyList<string> BuildStatusImmunities(int resistId)
        {
            if (!_statusResistsById.TryGetValue(resistId, out var entries))
            {
                return Array.Empty<string>();
            }

            return entries
                .Where(e => e.TriggerCoefficient >= 1000)
                .Select(e => StatusEffectTypes.TryGetValue(e.StatusConditionType, out var name)
                    ? name
                    : $"Status {e.StatusConditionType}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }

        private IReadOnlyList<string> BuildStatusImmunities(long resistId)
        {
            if (resistId > int.MaxValue || resistId < int.MinValue)
            {
                return Array.Empty<string>();
            }

            return BuildStatusImmunities((int)resistId);
        }

        private IReadOnlyList<string> BuildBuffImmunities(long resistId)
        {
            if (!_buffResistsById.TryGetValue(resistId, out var entries))
            {
                return Array.Empty<string>();
            }

            return entries
                .Where(e => e.TriggerCoefficient >= 1000)
                .Where(e => BuffDebuffTypes.ContainsKey(e.BuffDebuffType))
                .Select(e => BuffDebuffTypes[e.BuffDebuffType])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }

        private string BuildDescription(int levelConstantGroupId)
        {
            if (!_levelConstantsByGroup.TryGetValue(levelConstantGroupId, out var constant))
            {
                return string.Empty;
            }

            var raw = _localizationStore.Get(constant.BossCautionLanguageId);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var normalized = raw.Replace("\\n", "\n");
            normalized = WebUtility.HtmlDecode(normalized);
            normalized = BreakTagRegex.Replace(normalized, "\n");
            normalized = ParagraphCloseTagRegex.Replace(normalized, "\n\n");

            var cleaned = MarkupRegex.Replace(normalized, string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim();

            return cleaned;
        }

        private static long ComputeStat(long baseValue, int coefficient)
        {
            return (long)Math.Round(baseValue * (coefficient / 1000.0));
        }

        private static long ComputeHp(long baseHp, int hpCoefficient, long baseLbHp, int lbHpCoefficient)
        {
            return ComputeStat(baseHp, hpCoefficient) + ComputeStat(baseLbHp, lbHpCoefficient);
        }

        private LocalizationStore LoadLocalization(string path)
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Localization file not found at {Path}", path);
                return new LocalizationStore(new Dictionary<long, string>());
            }

            try
            {
                var json = File.ReadAllText(path);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions) ?? new();
                var map = new Dictionary<long, string>();
                foreach (var kvp in raw)
                {
                    if (long.TryParse(kvp.Key, out var id))
                    {
                        map[id] = kvp.Value;
                    }
                }

                return new LocalizationStore(map);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse localization file");
                return new LocalizationStore(new Dictionary<long, string>());
            }
        }

        private void LoadEventBattleLocalization(string path)
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Event battle localization file not found at {Path}", path);
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var payload = JsonSerializer.Deserialize<EventLocalizationPayloadRaw>(json, _jsonOptions);
                if (payload?.Data == null || payload.Data.Count == 0)
                {
                    return;
                }

                foreach (var entry in payload.Data)
                {
                    if (entry.Id <= 0 || string.IsNullOrWhiteSpace(entry.Value))
                    {
                        continue;
                    }

                    _eventBattleLocalization[entry.Id] = entry.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse event battle localization file {Path}", path);
            }
        }

        private List<T> LoadList<T>(string path)
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Data file not found: {Path}", path);
                return new List<T>();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? new List<T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load data file {Path}", path);
                return new List<T>();
            }
        }

        private sealed class EnemyRecord
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int LevelParameterGroupId { get; set; }
            public int LevelConstantGroupId { get; set; }
            public int HpCoefficient { get; set; }
            public int LbHpCoefficient { get; set; }
            public int PhysicalAttackCoefficient { get; set; }
            public int MagicalAttackCoefficient { get; set; }
            public int PhysicalDefenseCoefficient { get; set; }
            public int MagicalDefenseCoefficient { get; set; }
            public long ResistElementId { get; set; }
            public int ResistStatusConditionId { get; set; }
            public long ResistBuffDebuffId { get; set; }
            public IReadOnlyList<string> Species { get; set; } = Array.Empty<string>();
            public string SpeciesSummary { get; set; } = string.Empty;
            public IReadOnlyList<string> StageNames { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> SoloStageNames { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> CoopStageNames { get; set; } = Array.Empty<string>();
            public string StageSummary { get; set; } = string.Empty;
        }

        private sealed class StageNameAccumulator
        {
            public HashSet<string> All { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Solo { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Coop { get; } = new(StringComparer.OrdinalIgnoreCase);

            public StageNamesByMode ToStageNamesByMode()
            {
                return new StageNamesByMode
                {
                    All = All.OrderBy(n => n).ToList(),
                    Solo = Solo.OrderBy(n => n).ToList(),
                    Coop = Coop.OrderBy(n => n).ToList()
                };
            }
        }

        private sealed class StageNamesByMode
        {
            public static StageNamesByMode Empty { get; } = new();
            public IReadOnlyList<string> All { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> Solo { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> Coop { get; set; } = Array.Empty<string>();
        }

        private sealed class EnemyRaw
        {
            public int Id { get; set; }
            public long NameLanguageId { get; set; }
            public int EnemyLevelParameterBaseGroupId { get; set; }
            public int EnemyLevelConstantGroupId { get; set; }
            public int HpCoefficient { get; set; }
            public int LbHpCoefficient { get; set; }
            public int PhysicalAttackCoefficient { get; set; }
            public int MagicalAttackCoefficient { get; set; }
            public int PhysicalDefenseCoefficient { get; set; }
            public int MagicalDefenseCoefficient { get; set; }
            public long ResistElementId { get; set; }
            public int ResistStatusConditionId { get; set; }
            public long ResistBuffDebuffId { get; set; }
            public int SpeciesGroupId { get; set; }
        }

        private sealed class BattleEnemyRaw
        {
            public int EnemyId { get; set; }
            public int Level { get; set; }
            public long EnemyGroupId { get; set; }
        }

        private sealed class SoloAreaBattleRaw
        {
            public int Id { get; set; }
            public long BattleId { get; set; }
            public long NameLanguageId { get; set; }
        }

        private sealed class EventSoloBattleRaw
        {
            public int Id { get; set; }
            public long BattleId { get; set; }
            public long NameLanguageId { get; set; }
        }

        private sealed class EventMultiBattleRaw
        {
            public int Id { get; set; }
            public long BattleId { get; set; }
            public long NameLanguageId { get; set; }
        }

        private sealed class BattleRaw
        {
            public long Id { get; set; }
            public long WaveGroupId { get; set; }
        }

        private sealed class BattleWaveRaw
        {
            public long WaveGroupId { get; set; }
            public long EnemyGroupId { get; set; }
        }

        private sealed class EnemyLevelParameterBaseRaw
        {
            public int EnemyLevelParameterBaseGroupId { get; set; }
            public int Level { get; set; }
            public long BaseHp { get; set; }
            public long BaseLbHp { get; set; }
            public long BasePhysicalAttack { get; set; }
            public long BaseMagicalAttack { get; set; }
            public long BasePhysicalDefense { get; set; }
            public long BaseMagicalDefense { get; set; }
        }

        private sealed class EnemyLevelConstantRaw
        {
            public int EnemyLevelConstantGroupId { get; set; }
            public int Level { get; set; }
            public long BossCautionLanguageId { get; set; }
        }

        private sealed class ResistElementRaw
        {
            public long Id { get; set; }
            public int ElementType { get; set; }
            public int DamageCoefficient { get; set; }
        }

        private sealed class ResistStatusConditionRaw
        {
            public int Id { get; set; }
            public int StatusConditionType { get; set; }
            public int TriggerCoefficient { get; set; }
        }

        private sealed class ResistBuffDebuffRaw
        {
            public long Id { get; set; }
            public int BuffDebuffType { get; set; }
            public int TriggerCoefficient { get; set; }
        }

        private sealed class SpeciesRaw
        {
            public int Id { get; set; }
            public long NameLanguageId { get; set; }
        }

        private sealed class SpeciesGroupRelRaw
        {
            public int SpeciesGroupId { get; set; }
            public int SpeciesId { get; set; }
        }

        private sealed class LocalizationStore
        {
            private readonly IReadOnlyDictionary<long, string> _map;

            public LocalizationStore(IReadOnlyDictionary<long, string> map)
            {
                _map = map;
            }

            public string Get(long id)
            {
                if (_map.TryGetValue(id, out var value))
                {
                    return value;
                }

                return string.Empty;
            }
        }

        private sealed class EventLocalizationPayloadRaw
        {
            public List<EventLocalizationEntryRaw> Data { get; set; } = new();
        }

        private sealed class EventLocalizationEntryRaw
        {
            public long Id { get; set; }
            public string Value { get; set; } = string.Empty;
        }
    }
}
