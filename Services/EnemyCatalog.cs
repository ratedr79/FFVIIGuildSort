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
        private readonly ILogger<EnemyCatalog> _logger;
        private readonly Dictionary<int, EnemyRecord> _enemies = new();
        private readonly Dictionary<int, List<int>> _levelsByEnemyId = new();
        private readonly Dictionary<int, List<EnemyLevelParameterBaseRaw>> _levelParamsByGroup = new();
        private readonly Dictionary<int, EnemyLevelConstantRaw> _levelConstantsByGroup = new();
        private readonly Dictionary<int, IReadOnlyList<string>> _speciesByGroup = new();
        private readonly Dictionary<long, List<ResistElementRaw>> _elementResistsById = new();
        private readonly Dictionary<int, List<ResistStatusConditionRaw>> _statusResistsById = new();
        private readonly Dictionary<long, List<ResistBuffDebuffRaw>> _buffResistsById = new();
        private readonly LocalizationStore _localizationStore;
        private readonly ConcurrentDictionary<(int enemyId, int level), EnemyDetailView> _detailCache = new();
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static readonly Regex MarkupRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex BreakTagRegex = new(@"<\s*br\s*/?\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ParagraphCloseTagRegex = new(@"<\s*/\s*p\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<int, string> ElementNames = new()
        {
            { 2, "Fire" },
            { 3, "Ice" },
            { 4, "Water" },
            { 5, "Earth" },
            { 6, "Lightning" },
            { 7, "Wind" },
            { 8, "Holy" },
            { 9, "Dark" }
        };

        private static readonly Dictionary<int, string> StatusEffectTypes = new()
        {
            { 1, "Poison" },
            { 2, "Blind" },
            { 3, "Silence" },
            { 4, "Darkness" },
            { 5, "Stun" },
            { 6, "Fatigue" },
            { 7, "Fog" },
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
            { 12, "Fire Resistance Down" },
            { 14, "Ice Resistance Down" },
            { 16, "Lightning Resistance Down" },
            { 18, "Earth Resistance Down" },
            { 20, "Water Resistance Down" },
            { 22, "Wind Resistance Down" },
            { 24, "Phys. Damage Down" },
            { 26, "Mag. Damage Down" }
        };

        public EnemyCatalog(ILogger<EnemyCatalog> logger, IWebHostEnvironment environment)
        {
            _logger = logger;

            var contentRoot = environment.ContentRootPath;
            var basePath = Path.Combine(contentRoot, "external", "UnknownX7", "FF7EC-Data");
            var localizationPath = Path.Combine(basePath, "Localization", "en.json");
            _localizationStore = LoadLocalization(localizationPath);

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

        public IReadOnlyList<EnemySearchResult> SearchEnemies(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || _enemies.Count == 0)
            {
                return Array.Empty<EnemySearchResult>();
            }

            query = query.Trim();
            var results = new List<EnemySearchResult>();

            foreach (var record in _enemies.Values)
            {
                var matchesName = record.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchingStageNames = record.StageNames.Count == 0
                    ? new List<string>()
                    : record.StageNames
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
                    StageSummary = record.StageSummary,
                    DisplayName = record.Name,
                    DisplayLevelText = level.ToString(),
                    IsStageResult = false
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
                    StageSummary = record.StageSummary,
                    DisplayName = stageName,
                    DisplayLevelText = "N/A",
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

            var enemyStageNames = BuildStageNameLookup(soloAreaBattles, battles, battleWaves, battleEnemies);

            foreach (var enemy in enemies)
            {
                var stageNames = enemyStageNames.TryGetValue(enemy.Id, out var stageList)
                    ? stageList
                    : Array.Empty<string>();
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
                    StageNames = stageNames,
                    StageSummary = BuildStageSummary(stageNames)
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

        private Dictionary<int, IReadOnlyList<string>> BuildStageNameLookup(
            IReadOnlyList<SoloAreaBattleRaw> soloAreaBattles,
            IReadOnlyList<BattleRaw> battles,
            IReadOnlyList<BattleWaveRaw> battleWaves,
            IReadOnlyList<BattleEnemyRaw> battleEnemies)
        {
            if (soloAreaBattles.Count == 0 || battles.Count == 0 || battleWaves.Count == 0 || battleEnemies.Count == 0)
            {
                return new();
            }

            var stageNamesByBattleId = soloAreaBattles
                .Select(s => new
                {
                    s.BattleId,
                    Name = _localizationStore.Get(s.NameLanguageId)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .ToLookup(x => x.BattleId, x => x.Name);

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

            var stageNamesByEnemy = new Dictionary<int, List<string>>();

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

                var stageNameList = stage.ToList();
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
                            list = new List<string>();
                            stageNamesByEnemy[enemyId] = list;
                        }

                        foreach (var stageName in stageNameList)
                        {
                            if (!string.IsNullOrWhiteSpace(stageName) && !list.Any(existing => existing.Equals(stageName, StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(stageName);
                            }
                        }
                    }
                }
            }

            return stageNamesByEnemy.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList());
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
                    Type = ElementNames.TryGetValue(e.ElementType, out var name) ? name : $"Element {e.ElementType}",
                    Value = $"{e.DamageCoefficient / 10.0:0.#}%"
                })
                .ToList();
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
                .Select(e => BuffDebuffTypes.TryGetValue(e.BuffDebuffType, out var name)
                    ? name
                    : $"Buff/Debuff {e.BuffDebuffType}")
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
            public string StageSummary { get; set; } = string.Empty;
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
    }
}
