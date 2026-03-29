using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFVIIEverCrisisAnalyzer.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class WeaponSearchDataService
    {
        private const int MaxDisplayReleaseCount = 11; // Lv.130 / OB10
        private const int MaxDisplayUpgradeLevel = 10;
        private const int MaxDisplayLevel = 130;
        private const string MaxPassiveSourceLabel = "Lv.130 / OB10";
        private const int MinCustomizationRarityType = 3;

        private static readonly Dictionary<int, string> WeaponEvolveSlotNames = new()
        {
            { 1, "Heart" },
            { 2, "Spade" },
            { 3, "Diamond" },
            { 4, "Club" }
        };

        private readonly ILogger<WeaponSearchDataService> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly List<WeaponSearchItem> _allWeapons = new();

        // Lookup data retained for the snapshot API
        private Dictionary<int, WeaponRaw> _weaponsById = new();
        private Dictionary<int, string> _charactersById = new();
        private Dictionary<int, List<WeaponUpgradeSkillRaw>> _upgradesByWeapon = new();
        private Dictionary<int, WeaponRarityRaw> _weaponRarities = new();
        private Dictionary<int, List<WeaponRarityReleaseSkillRaw>> _rarityReleaseSkills = new();
        private WeaponStatCalculator? _statCalculator;
        private LocalizationStore? _locStore;
        private Dictionary<int, SkillWeaponRaw> _skillWeapons = new();
        private Dictionary<int, SkillActiveRaw> _skillActives = new();
        private Dictionary<int, SkillBaseRaw> _skillBases = new();
        private Dictionary<long, List<SkillEffectGroupEntryRaw>> _skillEffectGroups = new();
        private Dictionary<long, SkillEffectRaw> _skillEffects = new();
        private Dictionary<long, SkillEffectDescriptionRaw> _skillEffectDescriptions = new();
        private Dictionary<long, List<SkillEffectDescriptionGroupEntryRaw>> _skillEffectDescriptionGroups = new();
        private Dictionary<long, SkillDamageEffectRaw> _skillDamageEffects = new();
        private Dictionary<long, SkillAdditionalEffectRaw> _skillAdditionalEffects = new();
        private Dictionary<long, SkillStatusChangeEffectRaw> _skillStatusChangeEffects = new();
        private Dictionary<long, SkillStatusConditionEffectRaw> _skillStatusConditionEffects = new();
        private Dictionary<long, SkillBuffDebuffRaw> _skillBuffDebuffs = new();
        private Dictionary<long, SkillBuffDebuffEnhanceRaw> _skillBuffDebuffEnhances = new();
        private Dictionary<long, SkillCancelEffectRaw> _skillCancelEffects = new();
        private Dictionary<long, SkillAtbChangeEffectRaw> _skillAtbChanges = new();
        private Dictionary<long, SkillSpecialGaugeChangeEffectRaw> _skillSpecialGaugeChanges = new();
        private Dictionary<long, SkillTacticsGaugeChangeEffectRaw> _skillTacticsGaugeChanges = new();
        private Dictionary<long, SkillOveraccelGaugeChangeEffectRaw> _skillOveraccelGaugeChanges = new();
        private Dictionary<long, SkillCostumeCountChangeEffectRaw> _skillCostumeCountChanges = new();
        private Dictionary<long, SkillTriggerConditionHpRaw> _skillTriggerConditionHp = new();
        private Dictionary<long, List<int>> _buffDebuffGroups = new();
        private Dictionary<long, List<int>> _statusConditionGroups = new();
        private Dictionary<long, List<int>> _statusChangeGroups = new();
        private Dictionary<int, SkillPassiveRaw> _skillPassives = new();
        private Dictionary<int, List<WeaponEvolveRaw>> _weaponEvolves = new();
        private Dictionary<int, List<WeaponEvolveEffectRaw>> _weaponEvolveEffects = new();
        private Dictionary<int, Dictionary<int, WeaponEvolveWeaponSkillRaw>> _weaponEvolveWeaponSkills = new();
        private Dictionary<int, List<WeaponReleaseSettingRaw>> _weaponReleaseSettings = new();

        public WeaponSearchDataService(ILogger<WeaponSearchDataService> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
            LoadData();
        }

        public IReadOnlyList<WeaponSearchItem> GetWeapons(string? characterFilter = null)
        {
            if (string.IsNullOrWhiteSpace(characterFilter))
            {
                return _allWeapons;
            }

            return _allWeapons
                .Where(w => w.Character.Equals(characterFilter, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public IReadOnlyList<WeaponSearchItem> SearchWeapons(string searchText, string? characterFilter = null)
        {
            var baseWeapons = GetWeapons(characterFilter);
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return baseWeapons;
            }

            var query = searchText.Trim().ToLowerInvariant();
            return baseWeapons
                .Where(w =>
                    w.Name.ToLowerInvariant().Contains(query) ||
                    w.AbilityText.ToLowerInvariant().Contains(query))
                .ToList();
        }

        public WeaponSnapshotResult? GetWeaponSnapshot(string weaponId, int overboostLevel, int weaponLevel)
        {
            if (_statCalculator == null || _locStore == null)
                return null;

            var item = _allWeapons.FirstOrDefault(w => w.Id == weaponId);
            if (item == null)
                return null;

            if (!int.TryParse(weaponId, out var numericId) || !_weaponsById.TryGetValue(numericId, out var weapon))
                return null;

            var isUltimate = weapon.WeaponEquipmentType == 1;
            var upgradeCount = isUltimate ? 0 : Math.Clamp(overboostLevel, 0, 10);
            var level = Math.Clamp(weaponLevel, 1, 130);

            if (!_upgradesByWeapon.TryGetValue(numericId, out var weaponUpgrades))
                return null;

            var baseUpgrade = FindUpgrade(weaponUpgrades, isUltimate ? 0 : 1);
            if (baseUpgrade == null)
                return null;

            if (!_weaponRarities.TryGetValue(weapon.Id, out var weaponRarity))
                return null;

            // Resolve skill at the requested OB
            var skillUpgradeCount = upgradeCount;
            var skillUpgrade = FindUpgrade(weaponUpgrades, skillUpgradeCount)
                ?? weaponUpgrades.Where(u => u.UpgradeCount <= skillUpgradeCount)
                    .OrderByDescending(u => u.UpgradeCount)
                    .FirstOrDefault()
                ?? baseUpgrade;

            var selectedWeaponSkillId = skillUpgradeCount == 0
                ? weaponRarity.WeaponSkillId
                : skillUpgrade.WeaponSkillId;

            if (selectedWeaponSkillId == 0)
                selectedWeaponSkillId = weaponRarity.WeaponSkillId;

            if (!_skillWeapons.TryGetValue(selectedWeaponSkillId, out var skillWeapon))
                return null;

            _skillActives.TryGetValue(skillWeapon.SkillActiveId, out var skillActive);

            SkillBaseRaw? skillBase = null;
            if (skillActive != null && _skillBases.TryGetValue(skillActive.SkillBaseId, out var sb))
                skillBase = sb;
            else if (skillWeapon.SkillActiveId != 0 && _skillBases.TryGetValue(skillWeapon.SkillActiveId, out var sbFallback))
                skillBase = sbFallback;
            else if (_skillBases.TryGetValue(skillWeapon.Id, out var sbByWeaponSkill))
                skillBase = sbByWeaponSkill;

            // Build ability text
            string abilityText = string.Empty;
            double damagePercent = 0;
            if (skillBase != null)
            {
                var abilityType = ResolveAttackType(skillBase.BaseAttackType);
                var element = ResolveElement(skillBase.ElementType);
                var effectGroupId = (long)skillBase.SkillEffectGroupId;
                if (effectGroupId == 0 && isUltimate && skillBase.SkillBaseGroupId != 0)
                    effectGroupId = skillBase.SkillBaseGroupId;

                var effectEntries = _skillEffectGroups.TryGetValue(effectGroupId, out var entries)
                    ? entries : new List<SkillEffectGroupEntryRaw>();

                var details = BuildAbilityDetails(
                    effectEntries, _skillEffects, _skillEffectDescriptions, _skillEffectDescriptionGroups,
                    _skillDamageEffects, _skillAdditionalEffects, _skillStatusChangeEffects,
                    _skillStatusConditionEffects, _skillBuffDebuffs, _skillBuffDebuffEnhances,
                    _skillCancelEffects, _skillAtbChanges, _skillSpecialGaugeChanges,
                    _skillTacticsGaugeChanges, _skillOveraccelGaugeChanges, _skillCostumeCountChanges,
                    _skillTriggerConditionHp, _buffDebuffGroups, _statusConditionGroups,
                    _statusChangeGroups, _locStore, abilityType, element);

                abilityText = details.Text;
                damagePercent = details.DamagePercent;
            }

            // Compute stats
            var upgradeTypeForStats = baseUpgrade.WeaponUpgradeType == 0 ? 1 : baseUpgrade.WeaponUpgradeType;
            var statResult = _statCalculator.ComputeStats(weapon, level, upgradeCount, upgradeTypeForStats);

            // Compute R Abilities at the requested OB and level
            var releaseCount = GetReleaseCountForLevel(weapon, level);
            var passiveTotals = ComputePassiveTotalsAtLevel(weapon, weaponUpgrades, releaseCount, upgradeCount);

            // Compute customizations at the requested OB
            var customizations = BuildCustomizationsAtLevel(weapon, upgradeCount, damagePercent);

            return new WeaponSnapshotResult
            {
                Character = item.Character,
                Name = item.Name,
                EquipmentType = item.EquipmentType,
                AbilityText = abilityText,
                Patk = statResult.PhysicalAttack,
                Matk = statResult.MagicalAttack,
                Heal = statResult.HealingPower,
                RAbilities = passiveTotals,
                Customizations = customizations
            };
        }

        private void LoadData()
        {
            var contentRoot = _environment.ContentRootPath;
            var basePath = Path.Combine(contentRoot, "external", "UnknownX7", "FF7EC-Data");
            var localizationPath = Path.Combine(basePath, "Localization", "en.json");
            var masterPath = Path.Combine(basePath, "MasterData", "gl");

            if (!Directory.Exists(basePath))
            {
                _logger.LogWarning("FF7EC data directory not found at {Path}", basePath);
                return;
            }

            var localization = LoadLocalization(localizationPath);

            var weapons = LoadList<WeaponRaw>(Path.Combine(masterPath, "Weapon.json"));
            var characters = LoadList<CharacterRaw>(Path.Combine(masterPath, "Character.json"))
                .ToDictionary(c => c.Id, c => StripMarkup(localization.Get(c.NameLanguageId)));
            var materiaSupports = LoadList<MateriaSupportRaw>(Path.Combine(masterPath, "MateriaSupport.json"))
                .ToDictionary(m => m.Id, m => localization.Get(m.NameLanguageId));
            var upgradeSkills = LoadList<WeaponUpgradeSkillRaw>(Path.Combine(masterPath, "WeaponUpgradeSkill.json"));
            var upgradesByWeapon = upgradeSkills
                .GroupBy(u => u.WeaponId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.UpgradeCount).ToList());
            var upgradeLookup = upgradeSkills.ToDictionary(u => MakeUpgradeKey(u.WeaponId, u.UpgradeCount));

            var weaponLevels = LoadList<WeaponLevelRaw>(Path.Combine(masterPath, "WeaponLevel.json"));
            var weaponUpgradeParameters = LoadList<WeaponUpgradeParameterRaw>(Path.Combine(masterPath, "WeaponUpgradeParameter.json"));

            var weaponRarities = LoadList<WeaponRarityRaw>(Path.Combine(masterPath, "WeaponRarity.json"))
                .GroupBy(r => r.WeaponId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.RarityType).First());
            var rarityReleaseSkills = LoadList<WeaponRarityReleaseSkillRaw>(Path.Combine(masterPath, "WeaponRarityReleaseSkill.json"))
                .GroupBy(r => r.WeaponRarityId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(r => r.ReleaseCount).ToList());

            var statCalculator = new WeaponStatCalculator(weaponLevels, weaponUpgradeParameters, weaponRarities);

            var weaponEvolves = LoadList<WeaponEvolveRaw>(Path.Combine(masterPath, "WeaponEvolve.json"))
                .GroupBy(e => e.WeaponEvolveGroupId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var weaponEvolveEffects = LoadList<WeaponEvolveEffectRaw>(Path.Combine(masterPath, "WeaponEvolveEffect.json"))
                .GroupBy(e => e.WeaponEvolveId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var weaponEvolveWeaponSkills = LoadList<WeaponEvolveWeaponSkillRaw>(Path.Combine(masterPath, "WeaponEvolveWeaponSkill.json"))
                .GroupBy(s => s.WeaponEvolveWeaponSkillGroupId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(s => s.UpgradeCount));

            var skillWeapons = LoadList<SkillWeaponRaw>(Path.Combine(masterPath, "SkillWeapon.json"))
                .ToDictionary(s => s.Id);
            var skillActives = LoadList<SkillActiveRaw>(Path.Combine(masterPath, "SkillActive.json"))
                .ToDictionary(s => s.Id);
            var skillBases = LoadList<SkillBaseRaw>(Path.Combine(masterPath, "SkillBase.json"))
                .ToDictionary(s => s.Id);
            var skillEffectGroups = LoadList<SkillEffectGroupEntryRaw>(Path.Combine(masterPath, "SkillEffectGroup.json"))
                .GroupBy(e => e.Id)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Seq).ToList());
            var skillEffects = LoadList<SkillEffectRaw>(Path.Combine(masterPath, "SkillEffect.json"))
                .ToDictionary(e => e.Id);
            var skillDamageEffects = LoadList<SkillDamageEffectRaw>(Path.Combine(masterPath, "SkillDamageEffect.json"))
                .ToDictionary(d => d.Id);
            var skillAdditionalEffects = LoadList<SkillAdditionalEffectRaw>(Path.Combine(masterPath, "SkillAdditionalEffect.json"))
                .ToDictionary(d => d.Id);
            var skillStatusChangeEffects = LoadList<SkillStatusChangeEffectRaw>(Path.Combine(masterPath, "SkillStatusChangeEffect.json"))
                .ToDictionary(d => d.Id);
            var skillStatusConditionEffects = LoadList<SkillStatusConditionEffectRaw>(Path.Combine(masterPath, "SkillStatusConditionEffect.json"))
                .ToDictionary(d => d.Id);
            var skillBuffDebuffs = LoadList<SkillBuffDebuffRaw>(Path.Combine(masterPath, "SkillBuffDebuff.json"))
                .ToDictionary(d => d.Id);
            var skillBuffDebuffEnhances = LoadList<SkillBuffDebuffEnhanceRaw>(Path.Combine(masterPath, "SkillBuffDebuffEnhance.json"))
                .ToDictionary(d => d.Id);
            var skillCancelEffects = LoadList<SkillCancelEffectRaw>(Path.Combine(masterPath, "SkillCancelEffect.json"))
                .ToDictionary(d => d.Id);
            var skillAtbChanges = LoadList<SkillAtbChangeEffectRaw>(Path.Combine(masterPath, "SkillAtbChangeEffect.json"))
                .ToDictionary(d => d.Id);
            var skillSpecialGaugeChanges = LoadList<SkillSpecialGaugeChangeEffectRaw>(Path.Combine(masterPath, "SkillSpecialGaugeChangeEffect.json"))
                .ToDictionary(d => d.Id);
            var skillTacticsGaugeChanges = LoadList<SkillTacticsGaugeChangeEffectRaw>(Path.Combine(masterPath, "SkillTacticsGaugeChangeEffect.json"))
                .ToDictionary(d => d.Id);
            var skillOveraccelGaugeChanges = LoadList<SkillOveraccelGaugeChangeEffectRaw>(Path.Combine(masterPath, "SkillOveraccelGaugeChangeEffect.json"))
                .ToDictionary(d => d.Id);
            var skillCostumeCountChanges = LoadList<SkillCostumeCountChangeEffectRaw>(Path.Combine(masterPath, "SkillCostumeCountChangeEffect.json"))
                .ToDictionary(d => d.Id);
            var skillTriggerConditionHp = LoadList<SkillTriggerConditionHpRaw>(Path.Combine(masterPath, "SkillTriggerConditionHp.json"))
                .ToDictionary(d => d.Id);
            var skillLegendaries = LoadList<SkillLegendaryRaw>(Path.Combine(masterPath, "SkillLegendary.json"))
                .ToDictionary(s => s.Id);
            var skillNotesSets = LoadList<SkillNotesSetRaw>(Path.Combine(masterPath, "SkillNotesSet.json"))
                .GroupBy(s => s.Id)
                .ToDictionary(g => g.Key, g => g.First());
            var skillPassives = LoadList<SkillPassiveRaw>(Path.Combine(masterPath, "SkillPassive.json"))
                .ToDictionary(p => p.Id);
            var skillEffectDescriptions = LoadList<SkillEffectDescriptionRaw>(Path.Combine(masterPath, "SkillEffectDescription.json"))
                .ToDictionary(d => d.Id);
            var skillEffectDescriptionGroups = LoadList<SkillEffectDescriptionGroupEntryRaw>(Path.Combine(masterPath, "SkillEffectDescriptionGroup.json"))
                .GroupBy(e => e.Id)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Seq).ToList());
            var buffDebuffGroups = LoadList<BuffDebuffGroupEntryRaw>(Path.Combine(masterPath, "BuffDebuffGroup.json"))
                .GroupBy(e => e.Id)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SkillBuffDebuffType).ToList());
            var statusConditionGroups = LoadList<StatusConditionGroupEntryRaw>(Path.Combine(masterPath, "StatusConditionGroup.json"))
                .GroupBy(e => e.Id)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SkillStatusConditionType).ToList());
            var statusChangeGroups = LoadList<StatusChangeGroupEntryRaw>(Path.Combine(masterPath, "StatusChangeGroup.json"))
                .GroupBy(e => e.Id)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SkillStatusChangeType).ToList());

            var characterCostumes = LoadList<CharacterCostumeRaw>(Path.Combine(masterPath, "CharacterCostume.json"));
            var skillCharacterCostumes = LoadList<SkillCharacterCostumeRaw>(Path.Combine(masterPath, "SkillCharacterCostume.json"))
                .ToDictionary(c => c.Id);

            var materiaFallback = string.Empty;

            foreach (var weapon in weapons)
            {
                if (!characters.TryGetValue(weapon.CharacterId, out var characterName))
                {
                    characterName = $"Character #{weapon.CharacterId}";
                }

                if (!upgradesByWeapon.TryGetValue(weapon.Id, out var weaponUpgrades))
                {
                    continue;
                }

                var isUltimate = weapon.WeaponEquipmentType == 1;
                var baseUpgrade = FindUpgrade(weaponUpgrades, isUltimate ? 0 : 1);
                if (baseUpgrade == null)
                {
                    continue;
                }

                var skillForStats = FindUpgrade(weaponUpgrades, isUltimate ? 0 : 10) ?? baseUpgrade;
                if (!skillWeapons.TryGetValue(skillForStats.WeaponSkillId, out var skillWeapon))
                {
                    continue;
                }

                SkillActiveRaw? skillActive = null;
                if (skillActives.TryGetValue(skillWeapon.SkillActiveId, out var sa))
                {
                    skillActive = sa;
                }

                SkillBaseRaw? skillBase = null;
                if (skillActive != null && skillBases.TryGetValue(skillActive.SkillBaseId, out var sb))
                {
                    skillBase = sb;
                }
                else if (skillWeapon.SkillActiveId != 0 && skillBases.TryGetValue(skillWeapon.SkillActiveId, out var sbFallback))
                {
                    skillBase = sbFallback;
                }
                else if (skillBases.TryGetValue(skillWeapon.Id, out var sbByWeaponSkill))
                {
                    skillBase = sbByWeaponSkill;
                }

                if (skillBase == null)
                {
                    continue;
                }

                var abilityType = ResolveAttackType(skillBase.BaseAttackType);
                var element = ResolveElement(skillBase.ElementType);

                var effectGroupId = (long)skillBase.SkillEffectGroupId;
                if (effectGroupId == 0 && isUltimate && skillBase.SkillBaseGroupId != 0)
                {
                    effectGroupId = skillBase.SkillBaseGroupId;
                }

                var effectEntries = skillEffectGroups.TryGetValue(effectGroupId, out var entries)
                    ? entries
                    : new List<SkillEffectGroupEntryRaw>();

                if (!effectEntries.Any())
                {
                    _logger.LogWarning(
                        "Weapon {WeaponId} skill {SkillId} missing effect group {EffectGroupId}",
                        weapon.Id,
                        skillBase.Id,
                        effectGroupId);
                }

                var abilityDetails = BuildAbilityDetails(
                    effectEntries,
                    skillEffects,
                    skillEffectDescriptions,
                    skillEffectDescriptionGroups,
                    skillDamageEffects,
                    skillAdditionalEffects,
                    skillStatusChangeEffects,
                    skillStatusConditionEffects,
                    skillBuffDebuffs,
                    skillBuffDebuffEnhances,
                    skillCancelEffects,
                    skillAtbChanges,
                    skillSpecialGaugeChanges,
                    skillTacticsGaugeChanges,
                    skillOveraccelGaugeChanges,
                    skillCostumeCountChanges,
                    skillTriggerConditionHp,
                    buffDebuffGroups,
                    statusConditionGroups,
                    statusChangeGroups,
                    localization,
                    abilityType,
                    element);

                var effectTags = ExtractEffectTags(
                    effectEntries,
                    skillEffects,
                    skillStatusConditionEffects,
                    skillBuffDebuffs,
                    skillStatusChangeEffects);

                if (string.IsNullOrWhiteSpace(abilityDetails.Text))
                {
                    _logger.LogWarning(
                        "Weapon {WeaponId} produced empty ability text (group {GroupId}, effects {EffectCount})",
                        weapon.Id,
                        effectGroupId,
                        effectEntries.Count);
                }

                var equipmentType = isUltimate ? "Ultimate" : ResolveEquipmentType(weapon);

                var commandAtb = isUltimate ? 0 : skillActive?.Cost ?? 0;
                var commandSigil = ResolveCommandSigil(skillWeapon.SkillNotesSetId, skillNotesSets);

                var useCount = string.Empty;
                var rechargeTime = string.Empty;
                if (isUltimate && skillLegendaries.TryGetValue(skillForStats.WeaponSkillId, out var legendary))
                {
                    useCount = legendary.UseCountLimit > 0 ? legendary.UseCountLimit.ToString() : "Unlimited";
                    rechargeTime = legendary.RechargeTimeSec > 0 ? $"{legendary.RechargeTimeSec}s" : string.Empty;
                }
                else if (skillActive?.UseCountLimit > 0)
                {
                    useCount = skillActive.UseCountLimit.ToString();
                }

                var materia0 = weapon.WeaponMateriaSupportId0 != 0 && materiaSupports.TryGetValue(weapon.WeaponMateriaSupportId0, out var mat0)
                    ? mat0
                    : materiaFallback;
                var materia1 = weapon.WeaponMateriaSupportId1 != 0 && materiaSupports.TryGetValue(weapon.WeaponMateriaSupportId1, out var mat1)
                    ? mat1
                    : materiaFallback;
                var materia2 = weapon.WeaponMateriaSupportId2 != 0 && materiaSupports.TryGetValue(weapon.WeaponMateriaSupportId2, out var mat2)
                    ? mat2
                    : materiaFallback;

                var upgradeSkillData = BuildUpgradeSkillData(weapon, weaponUpgrades, skillPassives, localization);
                var maxPassiveTotals = BuildMaxPassiveTotals(
                    weapon,
                    weaponUpgrades,
                    weaponRarities,
                    rarityReleaseSkills,
                    skillPassives,
                    localization);

                if (weapon.Id == 1001)
                {
                    _logger.LogWarning(
                        "Buster Sword ability text: '{Ability}' (effects: {EffectCount}, damage: {DamagePercent})",
                        abilityDetails.Text,
                        effectEntries.Count,
                        abilityDetails.DamagePercent);
                }

                var upgradeTypeForStats = baseUpgrade.WeaponUpgradeType == 0 ? 1 : baseUpgrade.WeaponUpgradeType;
                var upgradeCountForStats = isUltimate ? 0 : MaxDisplayUpgradeLevel;
                var statResult = statCalculator.ComputeStats(
                    weapon,
                    MaxDisplayLevel,
                    upgradeCountForStats,
                    upgradeTypeForStats);

                if (statResult == WeaponStatResult.Empty)
                {
                    _logger.LogWarning(
                        "Failed to calculate stats for weapon {WeaponId} (levelGroup {LevelGroup}, upgradeGroup {UpgradeGroup})",
                        weapon.Id,
                        weapon.WeaponLevelGroupId,
                        weapon.WeaponUpgradeParameterGroupId);
                }

                var customizations = BuildWeaponCustomizations(
                    weapon,
                    weaponRarities,
                    weaponEvolves,
                    weaponEvolveEffects,
                    weaponEvolveWeaponSkills,
                    skillWeapons,
                    skillActives,
                    skillBases,
                    skillEffectGroups,
                    skillEffects,
                    skillEffectDescriptions,
                    skillEffectDescriptionGroups,
                    skillDamageEffects,
                    skillAdditionalEffects,
                    skillStatusChangeEffects,
                    skillStatusConditionEffects,
                    skillBuffDebuffs,
                    skillBuffDebuffEnhances,
                    skillCancelEffects,
                    skillAtbChanges,
                    skillSpecialGaugeChanges,
                    skillTacticsGaugeChanges,
                    skillOveraccelGaugeChanges,
                    skillCostumeCountChanges,
                    skillTriggerConditionHp,
                    buffDebuffGroups,
                    statusConditionGroups,
                    statusChangeGroups,
                    skillPassives,
                    localization,
                    abilityDetails.DamagePercent);

                var searchItem = new WeaponSearchItem
                {
                    Id = weapon.Id.ToString(),
                    Name = StripMarkup(localization.Get(weapon.NameLanguageId)),
                    Character = characterName,
                    Element = abilityDetails.IsHealing ? "Heal" : element,
                    DamagePercent = abilityDetails.DamagePercent,
                    Range = abilityDetails.Range,
                    AbilityText = abilityDetails.Text,
                    AbilityType = abilityType,
                    CommandAtb = commandAtb,
                    CommandSigil = commandSigil,
                    EquipmentType = equipmentType,
                    MateriaSupport0 = materia0,
                    MateriaSupport1 = materia1,
                    MateriaSupport2 = materia2,
                    RechargeTime = rechargeTime,
                    UseCount = useCount,
                    UpgradeSkills = upgradeSkillData,
                    MaxPassiveSkills = maxPassiveTotals,
                    MaxAbilityDescription = abilityDetails.Text,
                    EffectTags = effectTags,
                    PatkOb10Lv130 = statResult.PhysicalAttack,
                    MatkOb10Lv130 = statResult.MagicalAttack,
                    HealOb10Lv130 = statResult.HealingPower,
                    Customizations = customizations
                };

                _allWeapons.Add(searchItem);
            }

            AddCostumeEntries(
                characterCostumes,
                skillCharacterCostumes,
                characters,
                skillActives,
                skillBases,
                skillWeapons,
                skillEffectGroups,
                skillEffects,
                skillEffectDescriptions,
                skillEffectDescriptionGroups,
                skillDamageEffects,
                skillAdditionalEffects,
                skillStatusChangeEffects,
                skillStatusConditionEffects,
                skillBuffDebuffs,
                skillBuffDebuffEnhances,
                skillCancelEffects,
                skillAtbChanges,
                skillSpecialGaugeChanges,
                skillTacticsGaugeChanges,
                skillOveraccelGaugeChanges,
                skillCostumeCountChanges,
                skillTriggerConditionHp,
                buffDebuffGroups,
                statusConditionGroups,
                statusChangeGroups,
                localization,
                skillPassives,
                skillNotesSets);

            // Retain lookup data for snapshot API
            _weaponsById = weapons.ToDictionary(w => w.Id);
            _charactersById = characters;
            _upgradesByWeapon = upgradesByWeapon;
            _weaponRarities = weaponRarities;
            _rarityReleaseSkills = rarityReleaseSkills;
            _statCalculator = statCalculator;
            _locStore = localization;
            _skillWeapons = skillWeapons;
            _skillActives = skillActives;
            _skillBases = skillBases;
            _skillEffectGroups = skillEffectGroups;
            _skillEffects = skillEffects;
            _skillEffectDescriptions = skillEffectDescriptions;
            _skillEffectDescriptionGroups = skillEffectDescriptionGroups;
            _skillDamageEffects = skillDamageEffects;
            _skillAdditionalEffects = skillAdditionalEffects;
            _skillStatusChangeEffects = skillStatusChangeEffects;
            _skillStatusConditionEffects = skillStatusConditionEffects;
            _skillBuffDebuffs = skillBuffDebuffs;
            _skillBuffDebuffEnhances = skillBuffDebuffEnhances;
            _skillCancelEffects = skillCancelEffects;
            _skillAtbChanges = skillAtbChanges;
            _skillSpecialGaugeChanges = skillSpecialGaugeChanges;
            _skillTacticsGaugeChanges = skillTacticsGaugeChanges;
            _skillOveraccelGaugeChanges = skillOveraccelGaugeChanges;
            _skillCostumeCountChanges = skillCostumeCountChanges;
            _skillTriggerConditionHp = skillTriggerConditionHp;
            _buffDebuffGroups = buffDebuffGroups;
            _statusConditionGroups = statusConditionGroups;
            _statusChangeGroups = statusChangeGroups;
            _skillPassives = skillPassives;
            _weaponEvolves = weaponEvolves;
            _weaponEvolveEffects = weaponEvolveEffects;
            _weaponEvolveWeaponSkills = weaponEvolveWeaponSkills;
            _weaponReleaseSettings = LoadList<WeaponReleaseSettingRaw>(Path.Combine(masterPath, "WeaponReleaseSetting.json"))
                .GroupBy(r => r.WeaponReleaseSettingGroupId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.ReleaseCount).ToList());

            _logger.LogInformation("Loaded {Count} gear entries from JSON master data", _allWeapons.Count);
        }

        private void AddCostumeEntries(
            List<CharacterCostumeRaw> characterCostumes,
            Dictionary<int, SkillCharacterCostumeRaw> skillCharacterCostumes,
            Dictionary<int, string> characters,
            Dictionary<int, SkillActiveRaw> skillActives,
            Dictionary<int, SkillBaseRaw> skillBases,
            Dictionary<int, SkillWeaponRaw> skillWeapons,
            Dictionary<long, List<SkillEffectGroupEntryRaw>> skillEffectGroups,
            Dictionary<long, SkillEffectRaw> skillEffects,
            Dictionary<long, SkillEffectDescriptionRaw> skillEffectDescriptions,
            Dictionary<long, List<SkillEffectDescriptionGroupEntryRaw>> skillEffectDescriptionGroups,
            Dictionary<long, SkillDamageEffectRaw> skillDamageEffects,
            Dictionary<long, SkillAdditionalEffectRaw> skillAdditionalEffects,
            Dictionary<long, SkillStatusChangeEffectRaw> skillStatusChangeEffects,
            Dictionary<long, SkillStatusConditionEffectRaw> skillStatusConditionEffects,
            Dictionary<long, SkillBuffDebuffRaw> skillBuffDebuffs,
            Dictionary<long, SkillBuffDebuffEnhanceRaw> skillBuffDebuffEnhances,
            Dictionary<long, SkillCancelEffectRaw> skillCancelEffects,
            Dictionary<long, SkillAtbChangeEffectRaw> skillAtbChanges,
            Dictionary<long, SkillSpecialGaugeChangeEffectRaw> skillSpecialGaugeChanges,
            Dictionary<long, SkillTacticsGaugeChangeEffectRaw> skillTacticsGaugeChanges,
            Dictionary<long, SkillOveraccelGaugeChangeEffectRaw> skillOveraccelGaugeChanges,
            Dictionary<long, SkillCostumeCountChangeEffectRaw> skillCostumeCountChanges,
            Dictionary<long, SkillTriggerConditionHpRaw> skillTriggerConditionHp,
            Dictionary<long, List<int>> buffDebuffGroups,
            Dictionary<long, List<int>> statusConditionGroups,
            Dictionary<long, List<int>> statusChangeGroups,
            LocalizationStore localization,
            Dictionary<int, SkillPassiveRaw> skillPassives,
            Dictionary<long, SkillNotesSetRaw> skillNotesSets)
        {
            foreach (var costume in characterCostumes)
            {
                if (!characters.TryGetValue(costume.CharacterId, out var characterName))
                {
                    characterName = $"Character #{costume.CharacterId}";
                }

                SkillCharacterCostumeRaw? costumeSkill = null;
                if (costume.SkillCharacterCostumeId != 0)
                {
                    skillCharacterCostumes.TryGetValue(costume.SkillCharacterCostumeId, out costumeSkill);
                }

                SkillActiveRaw? costumeActive = null;
                SkillBaseRaw? costumeBase = null;
                SkillWeaponRaw? costumeWeaponSkill = null;

                if (costumeSkill != null)
                {
                    skillActives.TryGetValue(costumeSkill.SkillActiveId, out costumeActive);
                    if (costumeActive != null)
                    {
                        skillBases.TryGetValue(costumeActive.SkillBaseId, out costumeBase);
                        skillWeapons.TryGetValue(costumeActive.SkillBaseId, out costumeWeaponSkill);
                    }
                }

                string abilityType = string.Empty;
                string element = "Unknown";
                double damagePercent = 0;
                string range = "Unknown";
                string abilityText = string.Empty;
                var effectTags = new List<string>();

                if (costumeBase != null)
                {
                    abilityType = ResolveAttackType(costumeBase.BaseAttackType);
                    element = ResolveElement(costumeBase.ElementType);

                    var effectGroupId = (long)costumeBase.SkillEffectGroupId;
                    var effectEntries = skillEffectGroups.TryGetValue(effectGroupId, out var entries)
                        ? entries
                        : new List<SkillEffectGroupEntryRaw>();

                    var abilityDetails = BuildAbilityDetails(
                        effectEntries,
                        skillEffects,
                        skillEffectDescriptions,
                        skillEffectDescriptionGroups,
                        skillDamageEffects,
                        skillAdditionalEffects,
                        skillStatusChangeEffects,
                        skillStatusConditionEffects,
                        skillBuffDebuffs,
                        skillBuffDebuffEnhances,
                        skillCancelEffects,
                        skillAtbChanges,
                        skillSpecialGaugeChanges,
                        skillTacticsGaugeChanges,
                        skillOveraccelGaugeChanges,
                        skillCostumeCountChanges,
                        skillTriggerConditionHp,
                        buffDebuffGroups,
                        statusConditionGroups,
                        statusChangeGroups,
                        localization,
                        abilityType,
                        element);

                    abilityText = abilityDetails.Text;
                    damagePercent = abilityDetails.DamagePercent;
                    range = abilityDetails.Range;
                    element = abilityDetails.IsHealing ? "Heal" : element;

                    var costumeTags = ExtractEffectTags(
                        effectEntries,
                        skillEffects,
                        skillStatusConditionEffects,
                        skillBuffDebuffs,
                        skillStatusChangeEffects);
                    effectTags = costumeTags;
                }

                var commandAtb = costumeActive?.Cost ?? 0;
                var commandSigil = ResolveCommandSigil(costumeSkill?.SkillNotesSetId ?? 0, skillNotesSets);
                var useCount = costumeActive?.UseCountLimit > 0 ? costumeActive.UseCountLimit.ToString() : string.Empty;

                var searchItem = new WeaponSearchItem
                {
                    Id = $"costume-{costume.Id}",
                    Name = StripMarkup(localization.Get(costume.NameLanguageId)),
                    Character = characterName,
                    Element = element,
                    DamagePercent = damagePercent,
                    Range = range,
                    AbilityText = abilityText,
                    AbilityType = abilityType,
                    CommandAtb = commandAtb,
                    CommandSigil = commandSigil,
                    EquipmentType = "Costume",
                    MateriaSupport0 = string.Empty,
                    MateriaSupport1 = string.Empty,
                    MateriaSupport2 = string.Empty,
                    RechargeTime = string.Empty,
                    UseCount = useCount,
                    UpgradeSkills = new List<UpgradeSkillData>(),
                    MaxPassiveSkills = BuildCostumePassiveTotals(costume, skillPassives, localization),
                    MaxAbilityDescription = abilityText,
                    EffectTags = effectTags,
                    PatkOb10Lv130 = 0,
                    MatkOb10Lv130 = 0,
                    HealOb10Lv130 = 0,
                    Customizations = new List<WeaponCustomization>()
                };

                _allWeapons.Add(searchItem);
            }
        }

        private List<PassiveSkillTotal> BuildCostumePassiveTotals(
            CharacterCostumeRaw costume,
            Dictionary<int, SkillPassiveRaw> skillPassives,
            LocalizationStore localization)
        {
            var totals = new List<PassiveSkillTotal>();
            var passiveIds = new[] { costume.PassiveSkillId0, costume.PassiveSkillId1 };
            var passivePoints = new[] { costume.PassiveSkillPoint0, costume.PassiveSkillPoint1 };

            for (var slot = 0; slot < passiveIds.Length; slot++)
            {
                var passiveId = passiveIds[slot];
                var points = passivePoints[slot];
                if (passiveId == 0 || points == 0)
                {
                    continue;
                }

                var passiveName = skillPassives.TryGetValue(passiveId, out var passiveRaw)
                    ? localization.Get(passiveRaw.NameLanguageId)
                    : $"Passive {passiveId}";

                totals.Add(new PassiveSkillTotal
                {
                    SkillId = passiveId.ToString(),
                    SkillName = passiveName,
                    BasePoints = points,
                    UpgradePoints = 0,
                    TotalPoints = points,
                    SkillSlot = slot,
                    SourceLabel = "Costume"
                });
            }

            return totals;
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
                var dict = new Dictionary<long, string>();
                foreach (var kvp in raw)
                {
                    if (long.TryParse(kvp.Key, out var id))
                    {
                        dict[id] = kvp.Value;
                    }
                }
                return new LocalizationStore(dict);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to load localization data");
                return new LocalizationStore(new Dictionary<long, string>());
            }
        }

        private List<UpgradeSkillData> BuildUpgradeSkillData(
            WeaponRaw weapon,
            List<WeaponUpgradeSkillRaw> weaponUpgrades,
            Dictionary<int, SkillPassiveRaw> skillPassives,
            LocalizationStore localization)
        {
            var passiveIds = new[] { weapon.PassiveSkillId0, weapon.PassiveSkillId1, weapon.PassiveSkillId2 };
            var upgradeData = new List<UpgradeSkillData>();

            foreach (var upgrade in weaponUpgrades.OrderBy(u => u.UpgradeCount))
            {
                if (upgrade.UpgradeCount == 0)
                {
                    continue;
                }

                var passiveSkills = new List<PassiveSkillInfo>();
                var slotValues = new[]
                {
                    upgrade.AddPassiveSkillPoint0,
                    upgrade.AddPassiveSkillPoint1,
                    upgrade.AddPassiveSkillPoint2
                };

                for (var slot = 0; slot < slotValues.Length; slot++)
                {
                    var points = slotValues[slot];
                    if (points <= 0)
                    {
                        continue;
                    }

                    var passiveId = slot < passiveIds.Length ? passiveIds[slot] : 0;
                    if (passiveId == 0)
                    {
                        continue;
                    }

                    var passiveName = skillPassives.TryGetValue(passiveId, out var passiveRaw)
                        ? localization.Get(passiveRaw.NameLanguageId)
                        : $"Passive {passiveId}";

                    passiveSkills.Add(new PassiveSkillInfo
                    {
                        SkillId = passiveId.ToString(),
                        SkillName = passiveName,
                        SkillDescription = passiveName,
                        Points = points,
                        SkillSlot = slot
                    });
                }

                if (passiveSkills.Count > 0)
                {
                    upgradeData.Add(new UpgradeSkillData
                    {
                        UpgradeLevel = upgrade.UpgradeCount,
                        PassiveSkills = passiveSkills
                    });
                }
            }

            return upgradeData;
        }

        private List<PassiveSkillTotal> BuildMaxPassiveTotals(
            WeaponRaw weapon,
            List<WeaponUpgradeSkillRaw> weaponUpgrades,
            Dictionary<int, WeaponRarityRaw> weaponRarities,
            Dictionary<int, List<WeaponRarityReleaseSkillRaw>> rarityReleaseSkills,
            Dictionary<int, SkillPassiveRaw> skillPassives,
            LocalizationStore localization)
        {
            var totals = new List<PassiveSkillTotal>();
            var passiveIds = new[] { weapon.PassiveSkillId0, weapon.PassiveSkillId1, weapon.PassiveSkillId2 };

            if (!weaponRarities.TryGetValue(weapon.Id, out var rarity) ||
                !rarityReleaseSkills.TryGetValue(rarity.Id, out var releases) ||
                releases.Count == 0)
            {
                return totals;
            }

            var releaseEntry = releases.FirstOrDefault(r => r.ReleaseCount == MaxDisplayReleaseCount)
                ?? releases.LastOrDefault();

            var baseSlots = releaseEntry != null
                ? new[]
                {
                    releaseEntry.AddPassiveSkillPoint0,
                    releaseEntry.AddPassiveSkillPoint1,
                    releaseEntry.AddPassiveSkillPoint2
                }
                : new[] { 0, 0, 0 };

            var targetUpgrade = weaponUpgrades
                .Where(u => u.UpgradeCount == MaxDisplayUpgradeLevel)
                .OrderByDescending(u => u.UpgradeCount)
                .FirstOrDefault()
                ?? weaponUpgrades.OrderByDescending(u => u.UpgradeCount).FirstOrDefault();

            var upgradeSlots = targetUpgrade != null
                ? new[]
                {
                    targetUpgrade.AddPassiveSkillPoint0,
                    targetUpgrade.AddPassiveSkillPoint1,
                    targetUpgrade.AddPassiveSkillPoint2
                }
                : new[] { 0, 0, 0 };

            for (var slot = 0; slot < passiveIds.Length; slot++)
            {
                var passiveId = passiveIds[slot];
                if (passiveId == 0)
                {
                    continue;
                }

                var basePoints = baseSlots[slot];
                var upgradePoints = upgradeSlots[slot];
                var totalPoints = basePoints + upgradePoints;

                if (totalPoints == 0)
                {
                    continue;
                }

                var passiveName = skillPassives.TryGetValue(passiveId, out var passiveRaw)
                    ? localization.Get(passiveRaw.NameLanguageId)
                    : $"Passive {passiveId}";

                totals.Add(new PassiveSkillTotal
                {
                    SkillId = passiveId.ToString(),
                    SkillName = passiveName,
                    BasePoints = basePoints,
                    UpgradePoints = upgradePoints,
                    TotalPoints = totalPoints,
                    SkillSlot = slot,
                    SourceLabel = MaxPassiveSourceLabel
                });
            }

            return totals;
        }

        private static WeaponUpgradeSkillRaw? FindUpgrade(IEnumerable<WeaponUpgradeSkillRaw> upgrades, int upgradeCount)
        {
            return upgrades.FirstOrDefault(u => u.UpgradeCount == upgradeCount);
        }

        private int GetReleaseCountForLevel(WeaponRaw weapon, int level)
        {
            if (weapon.WeaponReleaseSettingGroupId == 0 ||
                !_weaponReleaseSettings.TryGetValue(weapon.WeaponReleaseSettingGroupId, out var settings) ||
                settings.Count == 0)
            {
                return 0;
            }

            // The release count is the minimum count where LevelLimit >= requested level
            var entry = settings.FirstOrDefault(s => s.LevelLimit >= level);
            return entry?.ReleaseCount ?? settings[^1].ReleaseCount;
        }

        private List<PassiveSkillTotal> ComputePassiveTotalsAtLevel(
            WeaponRaw weapon,
            List<WeaponUpgradeSkillRaw> weaponUpgrades,
            int targetReleaseCount,
            int targetUpgradeCount)
        {
            var totals = new List<PassiveSkillTotal>();
            var passiveIds = new[] { weapon.PassiveSkillId0, weapon.PassiveSkillId1, weapon.PassiveSkillId2 };

            if (!_weaponRarities.TryGetValue(weapon.Id, out var rarity) ||
                !_rarityReleaseSkills.TryGetValue(rarity.Id, out var releases) ||
                releases.Count == 0)
            {
                return totals;
            }

            var releaseEntry = releases.FirstOrDefault(r => r.ReleaseCount == targetReleaseCount)
                ?? releases.Where(r => r.ReleaseCount <= targetReleaseCount)
                    .OrderByDescending(r => r.ReleaseCount).FirstOrDefault()
                ?? releases.FirstOrDefault();

            var baseSlots = releaseEntry != null
                ? new[] { releaseEntry.AddPassiveSkillPoint0, releaseEntry.AddPassiveSkillPoint1, releaseEntry.AddPassiveSkillPoint2 }
                : new[] { 0, 0, 0 };

            var targetUpgrade = weaponUpgrades
                .FirstOrDefault(u => u.UpgradeCount == targetUpgradeCount)
                ?? (targetUpgradeCount > 0
                    ? weaponUpgrades.Where(u => u.UpgradeCount <= targetUpgradeCount && u.UpgradeCount > 0)
                        .OrderByDescending(u => u.UpgradeCount).FirstOrDefault()
                    : null);

            var upgradeSlots = targetUpgrade != null
                ? new[] { targetUpgrade.AddPassiveSkillPoint0, targetUpgrade.AddPassiveSkillPoint1, targetUpgrade.AddPassiveSkillPoint2 }
                : new[] { 0, 0, 0 };

            var sourceLabel = targetUpgradeCount == 0 ? "5\u2605" : $"OB{targetUpgradeCount}";

            // Pre-build a lookup of unlock levels per slot for locked indicator
            _weaponReleaseSettings.TryGetValue(weapon.WeaponReleaseSettingGroupId, out var releaseSettings);

            for (var slot = 0; slot < passiveIds.Length; slot++)
            {
                var passiveId = passiveIds[slot];
                if (passiveId == 0) continue;

                var basePoints = baseSlots[slot];
                var upgradePoints = upgradeSlots[slot];
                var totalPoints = basePoints + upgradePoints;

                var passiveName = _skillPassives.TryGetValue(passiveId, out var passiveRaw)
                    ? _locStore!.Get(passiveRaw.NameLanguageId)
                    : $"Passive {passiveId}";

                var isLocked = totalPoints == 0;
                int? lockedUntilLevel = null;

                if (isLocked && releaseSettings != null)
                {
                    // Find the first release count where this slot gains any points from leveling
                    var firstUnlockRelease = releases
                        .Where(r => slot == 0 ? r.AddPassiveSkillPoint0 > 0
                                  : slot == 1 ? r.AddPassiveSkillPoint1 > 0
                                              : r.AddPassiveSkillPoint2 > 0)
                        .OrderBy(r => r.ReleaseCount)
                        .FirstOrDefault();

                    if (firstUnlockRelease != null)
                    {
                        var settingEntry = releaseSettings
                            .FirstOrDefault(s => s.ReleaseCount == firstUnlockRelease.ReleaseCount);
                        lockedUntilLevel = settingEntry?.LevelLimit;
                    }
                }

                totals.Add(new PassiveSkillTotal
                {
                    SkillId = passiveId.ToString(),
                    SkillName = passiveName,
                    BasePoints = basePoints,
                    UpgradePoints = upgradePoints,
                    TotalPoints = totalPoints,
                    SkillSlot = slot,
                    SourceLabel = sourceLabel,
                    IsLocked = isLocked,
                    LockedUntilLevel = lockedUntilLevel
                });
            }

            return totals;
        }

        private List<WeaponCustomization> BuildCustomizationsAtLevel(
            WeaponRaw weapon,
            int targetUpgradeCount,
            double baseDamagePercent)
        {
            var results = new List<WeaponCustomization>();

            if (weapon.WeaponEvolveGroupId == 0) return results;
            if (!_weaponRarities.TryGetValue(weapon.Id, out var rarity) || rarity.RarityType < MinCustomizationRarityType) return results;
            if (!_weaponEvolves.TryGetValue(weapon.WeaponEvolveGroupId, out var evolveEntries)) return results;

            foreach (var evolve in evolveEntries)
            {
                if (!_weaponEvolveEffects.TryGetValue(evolve.Id, out var effects)) continue;

                var slot = WeaponEvolveSlotNames.TryGetValue(evolve.WeaponEvolveType, out var slotName)
                    ? slotName : $"Slot {evolve.WeaponEvolveType}";

                foreach (var effect in effects)
                {
                    string? description = effect.WeaponEvolveEffectType switch
                    {
                        1 => BuildEvolveAbilityAtLevel(effect, targetUpgradeCount, baseDamagePercent),
                        3 => BuildCustomizationPassiveDescription(effect.TargetId, _skillPassives, _locStore!),
                        _ => null
                    };

                    if (string.IsNullOrWhiteSpace(description)) continue;

                    var kind = effect.WeaponEvolveEffectType switch
                    {
                        1 => "Damage Upgrade",
                        3 => "R Ability",
                        _ => "Customization"
                    };

                    results.Add(new WeaponCustomization
                    {
                        Slot = slot,
                        Kind = kind,
                        Description = description
                    });
                }
            }

            return results;
        }

        private string? BuildEvolveAbilityAtLevel(
            WeaponEvolveEffectRaw effect,
            int targetUpgradeCount,
            double baseDamagePercent)
        {
            var skillGroupId = _weaponEvolveWeaponSkills.ContainsKey(effect.TargetId)
                ? effect.TargetId : effect.WeaponEvolveId;

            if (!_weaponEvolveWeaponSkills.TryGetValue(skillGroupId, out var upgrades) || upgrades.Count == 0)
                return null;

            if (!upgrades.TryGetValue(targetUpgradeCount, out var upgradeEntry))
            {
                upgradeEntry = upgrades.Where(kvp => kvp.Key <= targetUpgradeCount)
                    .OrderByDescending(kvp => kvp.Key)
                    .Select(kvp => kvp.Value)
                    .FirstOrDefault();
                if (upgradeEntry == null)
                    upgradeEntry = upgrades.OrderByDescending(kvp => kvp.Key).First().Value;
            }

            if (!_skillWeapons.TryGetValue(upgradeEntry.WeaponSkillId, out var skillWeapon))
                return null;

            SkillBaseRaw? evolveBase = null;
            if (skillWeapon.SkillActiveId != 0 && _skillActives.TryGetValue(skillWeapon.SkillActiveId, out var active))
            {
                _skillBases.TryGetValue(active.SkillBaseId, out evolveBase);
            }
            if (evolveBase == null && skillWeapon.SkillActiveId != 0 &&
                _skillBases.TryGetValue(skillWeapon.SkillActiveId, out var baseFromActiveId))
                evolveBase = baseFromActiveId;
            if (evolveBase == null && _skillBases.TryGetValue(skillWeapon.Id, out var baseByWeaponSkill))
                evolveBase = baseByWeaponSkill;
            if (evolveBase == null) return null;

            var abilityType = ResolveAttackType(evolveBase.BaseAttackType);
            var element = ResolveElement(evolveBase.ElementType);
            var effectGroupId = (long)evolveBase.SkillEffectGroupId;
            var entries = _skillEffectGroups.TryGetValue(effectGroupId, out var effectEntries)
                ? effectEntries : new List<SkillEffectGroupEntryRaw>();

            var abilityDetails = BuildAbilityDetails(
                entries, _skillEffects, _skillEffectDescriptions, _skillEffectDescriptionGroups,
                _skillDamageEffects, _skillAdditionalEffects, _skillStatusChangeEffects,
                _skillStatusConditionEffects, _skillBuffDebuffs, _skillBuffDebuffEnhances,
                _skillCancelEffects, _skillAtbChanges, _skillSpecialGaugeChanges,
                _skillTacticsGaugeChanges, _skillOveraccelGaugeChanges, _skillCostumeCountChanges,
                _skillTriggerConditionHp, _buffDebuffGroups, _statusConditionGroups,
                _statusChangeGroups, _locStore!, abilityType, element);

            if (abilityDetails.DamagePercent > 0 && baseDamagePercent > 0)
            {
                var diff = abilityDetails.DamagePercent - baseDamagePercent;
                if (diff > 0.1)
                    return $"Damage Potency +{diff:0.#}% (new {abilityDetails.DamagePercent:0.#}%)";
            }

            var abilityText = abilityDetails.Text.Replace("\n", " ").Trim();
            if (!string.IsNullOrWhiteSpace(abilityText)) return abilityText;
            if (abilityDetails.DamagePercent > 0)
                return $"Sets damage potency to {abilityDetails.DamagePercent:0.#}%";

            return null;
        }

        private List<WeaponCustomization> BuildWeaponCustomizations(
            WeaponRaw weapon,
            Dictionary<int, WeaponRarityRaw> weaponRarities,
            Dictionary<int, List<WeaponEvolveRaw>> weaponEvolves,
            Dictionary<int, List<WeaponEvolveEffectRaw>> weaponEvolveEffects,
            Dictionary<int, Dictionary<int, WeaponEvolveWeaponSkillRaw>> weaponEvolveWeaponSkills,
            Dictionary<int, SkillWeaponRaw> skillWeapons,
            Dictionary<int, SkillActiveRaw> skillActives,
            Dictionary<int, SkillBaseRaw> skillBases,
            Dictionary<long, List<SkillEffectGroupEntryRaw>> skillEffectGroups,
            Dictionary<long, SkillEffectRaw> skillEffects,
            Dictionary<long, SkillEffectDescriptionRaw> skillEffectDescriptions,
            Dictionary<long, List<SkillEffectDescriptionGroupEntryRaw>> skillEffectDescriptionGroups,
            Dictionary<long, SkillDamageEffectRaw> skillDamageEffects,
            Dictionary<long, SkillAdditionalEffectRaw> skillAdditionalEffects,
            Dictionary<long, SkillStatusChangeEffectRaw> skillStatusChangeEffects,
            Dictionary<long, SkillStatusConditionEffectRaw> skillStatusConditionEffects,
            Dictionary<long, SkillBuffDebuffRaw> skillBuffDebuffs,
            Dictionary<long, SkillBuffDebuffEnhanceRaw> skillBuffDebuffEnhances,
            Dictionary<long, SkillCancelEffectRaw> skillCancelEffects,
            Dictionary<long, SkillAtbChangeEffectRaw> skillAtbChanges,
            Dictionary<long, SkillSpecialGaugeChangeEffectRaw> skillSpecialGaugeChanges,
            Dictionary<long, SkillTacticsGaugeChangeEffectRaw> skillTacticsGaugeChanges,
            Dictionary<long, SkillOveraccelGaugeChangeEffectRaw> skillOveraccelGaugeChanges,
            Dictionary<long, SkillCostumeCountChangeEffectRaw> skillCostumeCountChanges,
            Dictionary<long, SkillTriggerConditionHpRaw> skillTriggerConditionHp,
            Dictionary<long, List<int>> buffDebuffGroups,
            Dictionary<long, List<int>> statusConditionGroups,
            Dictionary<long, List<int>> statusChangeGroups,
            Dictionary<int, SkillPassiveRaw> skillPassives,
            LocalizationStore localization,
            double baseDamagePercent)
        {
            var results = new List<WeaponCustomization>();

            if (weapon.WeaponEvolveGroupId == 0)
            {
                return results;
            }

            if (!weaponRarities.TryGetValue(weapon.Id, out var rarity) || rarity.RarityType < MinCustomizationRarityType)
            {
                return results;
            }

            if (!weaponEvolves.TryGetValue(weapon.WeaponEvolveGroupId, out var evolveEntries))
            {
                return results;
            }

            foreach (var evolve in evolveEntries)
            {
                if (!weaponEvolveEffects.TryGetValue(evolve.Id, out var effects))
                {
                    continue;
                }

                var slot = WeaponEvolveSlotNames.TryGetValue(evolve.WeaponEvolveType, out var slotName)
                    ? slotName
                    : $"Slot {evolve.WeaponEvolveType}";

                foreach (var effect in effects)
                {
                    string? description = effect.WeaponEvolveEffectType switch
                    {
                        1 => BuildCustomizationAbilityDescription(
                            effect,
                            weaponEvolveWeaponSkills,
                            skillWeapons,
                            skillActives,
                            skillBases,
                            skillEffectGroups,
                            skillEffects,
                            skillEffectDescriptions,
                            skillEffectDescriptionGroups,
                            skillDamageEffects,
                            skillAdditionalEffects,
                            skillStatusChangeEffects,
                            skillStatusConditionEffects,
                            skillBuffDebuffs,
                            skillBuffDebuffEnhances,
                            skillCancelEffects,
                            skillAtbChanges,
                            skillSpecialGaugeChanges,
                            skillTacticsGaugeChanges,
                            skillOveraccelGaugeChanges,
                            skillCostumeCountChanges,
                            skillTriggerConditionHp,
                            buffDebuffGroups,
                            statusConditionGroups,
                            statusChangeGroups,
                            localization,
                            baseDamagePercent),
                        3 => BuildCustomizationPassiveDescription(effect.TargetId, skillPassives, localization),
                        _ => null
                    };

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        continue;
                    }

                    var kind = effect.WeaponEvolveEffectType switch
                    {
                        1 => "Damage Upgrade",
                        3 => "R Ability",
                        _ => "Customization"
                    };

                    results.Add(new WeaponCustomization
                    {
                        Slot = slot,
                        Kind = kind,
                        Description = description
                    });
                }
            }

            return results;
        }

        private string? BuildCustomizationPassiveDescription(
            int passiveId,
            Dictionary<int, SkillPassiveRaw> skillPassives,
            LocalizationStore localization)
        {
            if (!skillPassives.TryGetValue(passiveId, out var passive))
            {
                return null;
            }

            var passiveName = StripMarkup(localization.Get(passive.NameLanguageId));
            if (string.IsNullOrWhiteSpace(passiveName))
            {
                passiveName = $"Passive {passive.Id}";
            }

            return $"Adds R Ability: {passiveName}";
        }

        private string? BuildCustomizationAbilityDescription(
            WeaponEvolveEffectRaw effect,
            Dictionary<int, Dictionary<int, WeaponEvolveWeaponSkillRaw>> weaponEvolveWeaponSkills,
            Dictionary<int, SkillWeaponRaw> skillWeapons,
            Dictionary<int, SkillActiveRaw> skillActives,
            Dictionary<int, SkillBaseRaw> skillBases,
            Dictionary<long, List<SkillEffectGroupEntryRaw>> skillEffectGroups,
            Dictionary<long, SkillEffectRaw> skillEffects,
            Dictionary<long, SkillEffectDescriptionRaw> skillEffectDescriptions,
            Dictionary<long, List<SkillEffectDescriptionGroupEntryRaw>> skillEffectDescriptionGroups,
            Dictionary<long, SkillDamageEffectRaw> skillDamageEffects,
            Dictionary<long, SkillAdditionalEffectRaw> skillAdditionalEffects,
            Dictionary<long, SkillStatusChangeEffectRaw> skillStatusChangeEffects,
            Dictionary<long, SkillStatusConditionEffectRaw> skillStatusConditionEffects,
            Dictionary<long, SkillBuffDebuffRaw> skillBuffDebuffs,
            Dictionary<long, SkillBuffDebuffEnhanceRaw> skillBuffDebuffEnhances,
            Dictionary<long, SkillCancelEffectRaw> skillCancelEffects,
            Dictionary<long, SkillAtbChangeEffectRaw> skillAtbChanges,
            Dictionary<long, SkillSpecialGaugeChangeEffectRaw> skillSpecialGaugeChanges,
            Dictionary<long, SkillTacticsGaugeChangeEffectRaw> skillTacticsGaugeChanges,
            Dictionary<long, SkillOveraccelGaugeChangeEffectRaw> skillOveraccelGaugeChanges,
            Dictionary<long, SkillCostumeCountChangeEffectRaw> skillCostumeCountChanges,
            Dictionary<long, SkillTriggerConditionHpRaw> skillTriggerConditionHp,
            Dictionary<long, List<int>> buffDebuffGroups,
            Dictionary<long, List<int>> statusConditionGroups,
            Dictionary<long, List<int>> statusChangeGroups,
            LocalizationStore localization,
            double baseDamagePercent)
        {
            var skillGroupId = weaponEvolveWeaponSkills.ContainsKey(effect.TargetId)
                ? effect.TargetId
                : effect.WeaponEvolveId;

            if (!weaponEvolveWeaponSkills.TryGetValue(skillGroupId, out var upgrades) || upgrades.Count == 0)
            {
                return null;
            }

            if (!upgrades.TryGetValue(MaxDisplayUpgradeLevel, out var upgradeEntry))
            {
                upgradeEntry = upgrades.OrderByDescending(kvp => kvp.Key).First().Value;
            }

            if (!skillWeapons.TryGetValue(upgradeEntry.WeaponSkillId, out var skillWeapon))
            {
                return null;
            }

            SkillBaseRaw? evolveBase = null;
            SkillActiveRaw? evolveActive = null;

            if (skillWeapon.SkillActiveId != 0 && skillActives.TryGetValue(skillWeapon.SkillActiveId, out var active))
            {
                evolveActive = active;
                if (skillBases.TryGetValue(active.SkillBaseId, out var baseRaw))
                {
                    evolveBase = baseRaw;
                }
            }

            if (evolveBase == null && skillWeapon.SkillActiveId != 0 &&
                skillBases.TryGetValue(skillWeapon.SkillActiveId, out var baseFromActiveId))
            {
                evolveBase = baseFromActiveId;
            }

            if (evolveBase == null && skillBases.TryGetValue(skillWeapon.Id, out var baseByWeaponSkill))
            {
                evolveBase = baseByWeaponSkill;
            }

            if (evolveBase == null)
            {
                return null;
            }

            var abilityType = ResolveAttackType(evolveBase.BaseAttackType);
            var element = ResolveElement(evolveBase.ElementType);
            var effectGroupId = (long)evolveBase.SkillEffectGroupId;
            var entries = skillEffectGroups.TryGetValue(effectGroupId, out var effectEntries)
                ? effectEntries
                : new List<SkillEffectGroupEntryRaw>();

            var abilityDetails = BuildAbilityDetails(
                entries,
                skillEffects,
                skillEffectDescriptions,
                skillEffectDescriptionGroups,
                skillDamageEffects,
                skillAdditionalEffects,
                skillStatusChangeEffects,
                skillStatusConditionEffects,
                skillBuffDebuffs,
                skillBuffDebuffEnhances,
                skillCancelEffects,
                skillAtbChanges,
                skillSpecialGaugeChanges,
                skillTacticsGaugeChanges,
                skillOveraccelGaugeChanges,
                skillCostumeCountChanges,
                skillTriggerConditionHp,
                buffDebuffGroups,
                statusConditionGroups,
                statusChangeGroups,
                localization,
                abilityType,
                element);

            if (abilityDetails.DamagePercent > 0 && baseDamagePercent > 0)
            {
                var diff = abilityDetails.DamagePercent - baseDamagePercent;
                if (diff > 0.1)
                {
                    return $"Damage Potency +{diff:0.#}% (new {abilityDetails.DamagePercent:0.#}%)";
                }
            }

            var abilityText = abilityDetails.Text.Replace("\n", " ").Trim();
            if (!string.IsNullOrWhiteSpace(abilityText))
            {
                return abilityText;
            }

            if (abilityDetails.DamagePercent > 0)
            {
                return $"Sets damage potency to {abilityDetails.DamagePercent:0.#}%";
            }

            return null;
        }

        private List<string> ExtractEffectTags(
            List<SkillEffectGroupEntryRaw> effectEntries,
            Dictionary<long, SkillEffectRaw> skillEffects,
            Dictionary<long, SkillStatusConditionEffectRaw> statusConditionEffects,
            Dictionary<long, SkillBuffDebuffRaw> buffDebuffs,
            Dictionary<long, SkillStatusChangeEffectRaw> statusChangeEffects)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in effectEntries)
            {
                if (!skillEffects.TryGetValue(entry.SkillEffectId, out var effect))
                {
                    continue;
                }

                switch (effect.SkillEffectType)
                {
                    case 2:
                        if (statusConditionEffects.TryGetValue(effect.SkillEffectDetailId, out var condition) &&
                            StatusEffectTypes.TryGetValue(condition.SkillStatusConditionType, out var statusName))
                        {
                            tags.Add(statusName);
                        }
                        break;
                    case 3:
                        if (buffDebuffs.TryGetValue(effect.SkillEffectDetailId, out var buff) &&
                            BuffDebuffTypes.TryGetValue(buff.SkillBuffDebuffType, out var buffName))
                        {
                            tags.Add(buffName);
                        }
                        break;
                    case 5:
                        if (statusChangeEffects.TryGetValue(effect.SkillEffectDetailId, out var change) &&
                            StatusChangeTypes.TryGetValue(change.SkillStatusChangeType, out var changeName))
                        {
                            tags.Add(changeName);
                        }
                        break;
                }
            }

            return tags.OrderBy(t => t).ToList();
        }

        private (string Text, double DamagePercent, string Range, bool IsHealing) BuildAbilityDetails(
            List<SkillEffectGroupEntryRaw> effectEntries,
            Dictionary<long, SkillEffectRaw> skillEffects,
            Dictionary<long, SkillEffectDescriptionRaw> descriptions,
            Dictionary<long, List<SkillEffectDescriptionGroupEntryRaw>> descriptionGroups,
            Dictionary<long, SkillDamageEffectRaw> damageEffects,
            Dictionary<long, SkillAdditionalEffectRaw> additionalEffects,
            Dictionary<long, SkillStatusChangeEffectRaw> statusChangeEffects,
            Dictionary<long, SkillStatusConditionEffectRaw> statusConditionEffects,
            Dictionary<long, SkillBuffDebuffRaw> buffDebuffs,
            Dictionary<long, SkillBuffDebuffEnhanceRaw> buffDebuffEnhances,
            Dictionary<long, SkillCancelEffectRaw> cancelEffects,
            Dictionary<long, SkillAtbChangeEffectRaw> atbChanges,
            Dictionary<long, SkillSpecialGaugeChangeEffectRaw> specialGaugeChanges,
            Dictionary<long, SkillTacticsGaugeChangeEffectRaw> tacticsGaugeChanges,
            Dictionary<long, SkillOveraccelGaugeChangeEffectRaw> overaccelGaugeChanges,
            Dictionary<long, SkillCostumeCountChangeEffectRaw> costumeCountChanges,
            Dictionary<long, SkillTriggerConditionHpRaw> triggerHpConditions,
            Dictionary<long, List<int>> buffDebuffGroups,
            Dictionary<long, List<int>> statusConditionGroups,
            Dictionary<long, List<int>> statusChangeGroups,
            LocalizationStore localization,
            string abilityType,
            string element)
        {
            var abilityTextLines = new List<string>();
            double? abilityPotency = null;
            var range = "Unknown";
            string? primaryBaseText = null;
            string primaryCondition = string.Empty;
            var primaryInsertAtTop = true;
            var isHealingAbility = false;

            foreach (var entry in effectEntries)
            {
                if (!skillEffects.TryGetValue(entry.SkillEffectId, out var effect))
                {
                    continue;
                }

                var isDamageEffect = effect.SkillEffectType == 1;
                if (isDamageEffect)
                {
                    if (damageEffects.TryGetValue(effect.SkillEffectDetailId, out var damage))
                    {
                        var targetType = effect.TargetType;
                        var isSupportTarget = targetType >= 3 && targetType <= 6;
                        range = ResolveTarget(targetType);
                        primaryCondition = BuildTriggerConditionPrefix(effect, triggerHpConditions);

                        if (isSupportTarget)
                        {
                            primaryInsertAtTop = false;
                            isHealingAbility = true;
                            if (damage.SkillDamageType == 1)
                            {
                                var healPot = damage.MaxDamageCoefficient / 22.0;
                                abilityPotency = healPot;
                                primaryBaseText = $"{abilityType} heal is cast [Pot: {healPot:0.#}% of Healing Pot.] [Rng.: {range}]";
                            }
                            else if (damage.SkillDamageType == 2)
                            {
                                var healPot = damage.MaxDamageCoefficient / 10.0;
                                abilityPotency = healPot;
                                primaryBaseText = $"Restores {healPot:0.#}% of max HP [{abilityType}] [Rng.: {range}]";
                            }
                            else
                            {
                                abilityPotency = damage.MaxDamageCoefficient / 10.0;
                                primaryBaseText = $"Restores HP [Rng.: {range}]";
                            }
                        }
                        else
                        {
                            primaryInsertAtTop = true;
                            abilityPotency = damage.MaxDamageCoefficient / 10.0;
                            primaryBaseText = $"Deal {abilityPotency.Value:0.#}% {abilityType} {element} damage [Rng.: {range}]";
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Missing damage effect detail {DetailId} for skill effect {EffectId}",
                            effect.SkillEffectDetailId,
                            effect.Id);
                    }

                    continue;
                }

                var isPrimaryEffect = abilityTextLines.Count == 0;
                var description = BuildEffectDescription(
                    effect,
                    descriptions,
                    descriptionGroups,
                    additionalEffects,
                    statusChangeEffects,
                    statusConditionEffects,
                    buffDebuffs,
                    buffDebuffEnhances,
                    cancelEffects,
                    atbChanges,
                    specialGaugeChanges,
                    tacticsGaugeChanges,
                    overaccelGaugeChanges,
                    costumeCountChanges,
                    triggerHpConditions,
                    buffDebuffGroups,
                    statusConditionGroups,
                    statusChangeGroups,
                    localization,
                    isPrimaryEffect);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    abilityTextLines.Add(description);
                }
            }

            if (!string.IsNullOrWhiteSpace(primaryBaseText))
            {
                var isPrimaryLine = primaryInsertAtTop || abilityTextLines.Count == 0;
                var formatted = FormatEffectWithCondition(primaryBaseText, primaryCondition, isPrimaryLine);
                if (primaryInsertAtTop || abilityTextLines.Count == 0)
                {
                    abilityTextLines.Insert(0, formatted);
                }
                else
                {
                    abilityTextLines.Add(formatted);
                }
            }

            return (
                abilityTextLines.Count > 0 ? string.Join("\n", abilityTextLines) : string.Empty,
                abilityPotency ?? 0,
                range,
                isHealingAbility);
        }

        private string BuildEffectDescription(
            SkillEffectRaw effect,
            Dictionary<long, SkillEffectDescriptionRaw> descriptions,
            Dictionary<long, List<SkillEffectDescriptionGroupEntryRaw>> descriptionGroups,
            Dictionary<long, SkillAdditionalEffectRaw> additionalEffects,
            Dictionary<long, SkillStatusChangeEffectRaw> statusChangeEffects,
            Dictionary<long, SkillStatusConditionEffectRaw> statusConditionEffects,
            Dictionary<long, SkillBuffDebuffRaw> buffDebuffs,
            Dictionary<long, SkillBuffDebuffEnhanceRaw> buffDebuffEnhances,
            Dictionary<long, SkillCancelEffectRaw> cancelEffects,
            Dictionary<long, SkillAtbChangeEffectRaw> atbChanges,
            Dictionary<long, SkillSpecialGaugeChangeEffectRaw> specialGaugeChanges,
            Dictionary<long, SkillTacticsGaugeChangeEffectRaw> tacticsGaugeChanges,
            Dictionary<long, SkillOveraccelGaugeChangeEffectRaw> overaccelGaugeChanges,
            Dictionary<long, SkillCostumeCountChangeEffectRaw> costumeCountChanges,
            Dictionary<long, SkillTriggerConditionHpRaw> triggerHpConditions,
            Dictionary<long, List<int>> buffDebuffGroups,
            Dictionary<long, List<int>> statusConditionGroups,
            Dictionary<long, List<int>> statusChangeGroups,
            LocalizationStore localization,
            bool isPrimaryEffect)
        {
            var conditionPrefix = BuildTriggerConditionPrefix(effect, triggerHpConditions);

            if (effect.SkillEffectDescriptionGroupId != 0 &&
                descriptionGroups.TryGetValue(effect.SkillEffectDescriptionGroupId, out var groupEntries))
            {
                var parts = new List<string>();
                foreach (var entry in groupEntries)
                {
                    if (descriptions.TryGetValue(entry.SkillEffectDescriptionId, out var description))
                    {
                        var text = StripMarkup(localization.Get(description.DescriptionLanguageId));
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            parts.Add(text);
                        }
                    }
                }

                if (parts.Count > 0)
                {
                    var baseText = string.Join(" ", parts);
                    return FormatEffectWithCondition(baseText, conditionPrefix, isPrimaryEffect);
                }
            }

            string effectText = effect.SkillEffectType switch
            {
                2 => BuildStatusConditionEffect(effect, statusConditionEffects),
                3 => BuildBuffDebuffEffect(effect, buffDebuffs),
                5 => BuildStatusChangeEffect(effect, statusChangeEffects),
                6 => BuildCancelEffect(effect, cancelEffects, buffDebuffGroups, statusConditionGroups, statusChangeGroups),
                7 => BuildCustomEffectDescription(effect, additionalEffects),
                16 => BuildAtbChangeEffect(effect, atbChanges),
                26 => BuildSpecialGaugeChangeEffect(effect, specialGaugeChanges),
                30 => BuildTacticsGaugeChangeEffect(effect, tacticsGaugeChanges),
                31 => BuildBuffDebuffEnhanceEffect(effect, buffDebuffEnhances),
                36 => BuildOveraccelGaugeChangeEffect(effect, overaccelGaugeChanges),
                37 => BuildCostumeCountChangeEffect(effect, costumeCountChanges),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(effectText))
            {
                effectText = BuildFallbackEffect(effect);
            }

            return FormatEffectWithCondition(effectText, conditionPrefix, isPrimaryEffect);
        }

        private string BuildStatusConditionEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillStatusConditionEffectRaw> statusConditionEffects)
        {
            if (!statusConditionEffects.TryGetValue(effect.SkillEffectDetailId, out var condition))
            {
                return string.Empty;
            }

            var name = StatusEffectTypes.TryGetValue(condition.SkillStatusConditionType, out var value)
                ? value
                : $"Status {condition.SkillStatusConditionType}";

            var parts = new List<string>
            {
                $"Applies {name}",
                $"[Rng.: {ResolveTarget(effect.TargetType)}]"
            };

            if (condition.EffectCoefficient != 0)
            {
                parts.Add($"[Pot: {condition.EffectCoefficient / 10.0:0.#}%]");
            }

            parts.Add($"[Dur: {condition.MaxDurationSec}s]");
            if (condition.MaxDuplicationDurationSec > 0)
            {
                parts.Add($"[Ext: {condition.MaxDuplicationDurationSec}s]");
            }

            if (condition.IgnoreResist)
            {
                parts.Add("(Ignores resist)");
            }

            return string.Join(' ', parts);
        }

        private string BuildBuffDebuffEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillBuffDebuffRaw> buffDebuffs)
        {
            if (!buffDebuffs.TryGetValue(effect.SkillEffectDetailId, out var buff))
            {
                return string.Empty;
            }

            var name = BuffDebuffTypes.TryGetValue(buff.SkillBuffDebuffType, out var value)
                ? value
                : $"Buff/Debuff {buff.SkillBuffDebuffType}";

            var parts = new List<string>
            {
                name,
                $"[Pot: {ResolveBuffTier(buff.TriggerEffectLevel)}]",
                $"[Rng.: {ResolveTarget(effect.TargetType)}]",
                $"[Dur: {buff.MaxDurationSec}s]"
            };

            if (buff.MaxDuplicationDurationSec > 0)
            {
                parts.Add($"[Ext: {buff.MaxDuplicationDurationSec}s]");
            }

            if (buff.TriggerEffectLevelMax > buff.TriggerEffectLevel)
            {
                parts.Add($"[Max Pot: {ResolveBuffTier(buff.TriggerEffectLevelMax)}]");
            }

            return string.Join(' ', parts);
        }

        private string BuildStatusChangeEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillStatusChangeEffectRaw> statusChangeEffects)
        {
            if (!statusChangeEffects.TryGetValue(effect.SkillEffectDetailId, out var change))
            {
                return string.Empty;
            }

            var name = StatusChangeTypes.TryGetValue(change.SkillStatusChangeType, out var value)
                ? value
                : $"Status Change {change.SkillStatusChangeType}";

            var parts = new List<string>
            {
                $"Applies {name}",
                $"[Rng.: {ResolveTarget(effect.TargetType)}]"
            };

            if (change.EffectCount != 0)
            {
                parts.Add($"[Count: {change.EffectCount}]");
            }

            var pot = BuildStatusChangePotText(change);
            if (!string.IsNullOrEmpty(pot))
            {
                parts.Add(pot);
            }

            if (change.MaxDurationSec > 0)
            {
                parts.Add($"[Dur: {change.MaxDurationSec}s]");
            }

            if (change.MaxDuplicationDurationSec > 0)
            {
                parts.Add($"[Ext: {change.MaxDuplicationDurationSec}s]");
            }

            return string.Join(' ', parts);
        }

        private static string BuildStatusChangePotText(SkillStatusChangeEffectRaw change)
        {
            var value = change.EffectCoefficient;
            return change.SkillStatusChangeType switch
            {
                6 => $"[Pot: +{value / 10.0:0.#}% of target's max HP]",
                10 => $"[When triggered: restores {value / 10.0:0.#}% of target's max HP]",
                12 or 13 => $"[Damage +{value / 10.0:0.#}% up to {change.EffectCount} time(s)]",
                14 => $"[Healing +{value / 10.0:0.#}% up to {change.EffectCount} time(s)]",
                44 => $"[Phys Weapon/Gear ATB Cost: -{value}]",
                45 => $"[Mag Weapon/Gear ATB Cost: -{value}]",
                _ when value != 0 => $"[Pot: {value / 10.0:0.#}%]",
                _ => string.Empty
            };
        }

        private string BuildCancelEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillCancelEffectRaw> cancelEffects,
            Dictionary<long, List<int>> buffDebuffGroups,
            Dictionary<long, List<int>> statusConditionGroups,
            Dictionary<long, List<int>> statusChangeGroups)
        {
            if (!cancelEffects.TryGetValue(effect.SkillEffectDetailId, out var cancel))
            {
                return string.Empty;
            }

            var entries = new List<string>();

            if (cancel.BuffDebuffGroupId != 0 &&
                buffDebuffGroups.TryGetValue(cancel.BuffDebuffGroupId, out var buffIds))
            {
                entries.AddRange(buffIds.Select(id => BuffDebuffTypes.TryGetValue(id, out var name) ? name : $"Buff/Debuff {id}"));
            }

            if (cancel.StatusConditionGroupId != 0 &&
                statusConditionGroups.TryGetValue(cancel.StatusConditionGroupId, out var conditionIds))
            {
                entries.AddRange(conditionIds.Select(id =>
                    StatusEffectTypes.TryGetValue(id, out var name)
                        ? $"Ailment: {name}"
                        : $"Ailment: {id}"));
            }

            if (cancel.StatusChangeGroupId != 0 &&
                statusChangeGroups.TryGetValue(cancel.StatusChangeGroupId, out var changeIds))
            {
                entries.AddRange(changeIds.Select(id => StatusChangeTypes.TryGetValue(id, out var name) ? name : $"Status Change {id}"));
            }

            if (entries.Count == 0)
            {
                return string.Empty;
            }

            return $"Removes {string.Join(", ", entries)} [Rng.: {ResolveTarget(effect.TargetType)}]";
        }

        private string BuildAtbChangeEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillAtbChangeEffectRaw> atbChanges)
        {
            if (!atbChanges.TryGetValue(effect.SkillEffectDetailId, out var atb))
            {
                return string.Empty;
            }

            return $"+{atb.Value} ATB Gauge [Rng.: {ResolveTarget(effect.TargetType)}]";
        }

        private string BuildSpecialGaugeChangeEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillSpecialGaugeChangeEffectRaw> specialGaugeChanges)
        {
            if (!specialGaugeChanges.TryGetValue(effect.SkillEffectDetailId, out var change))
            {
                return string.Empty;
            }

            var gaugeType = change.TargetSkillSpecialType >= 0 && change.TargetSkillSpecialType < SpecialGaugeTypes.Length
                ? SpecialGaugeTypes[change.TargetSkillSpecialType]
                : "Gauge";

            var action = change.SkillSpecialGaugeChangeType switch
            {
                1 => "Increases",
                2 => "Decreases",
                _ => "Modifies"
            };

            return $"{action} {gaugeType} by {change.PermilValue / 10.0:0.#}% [Rng.: {ResolveTarget(effect.TargetType)}]";
        }

        private string BuildTacticsGaugeChangeEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillTacticsGaugeChangeEffectRaw> tacticsGaugeChanges)
        {
            if (!tacticsGaugeChanges.TryGetValue(effect.SkillEffectDetailId, out var change))
            {
                return string.Empty;
            }

            var action = change.SkillEffectGaugeChangeType switch
            {
                1 => "Increases",
                2 => "Decreases",
                _ => "Modifies"
            };

            return $"{action} Command Gauge [Pot: {change.PermilValue / 10.0:0.#}%]";
        }

        private string BuildBuffDebuffEnhanceEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillBuffDebuffEnhanceRaw> buffDebuffEnhances)
        {
            if (!buffDebuffEnhances.TryGetValue(effect.SkillEffectDetailId, out var enhance))
            {
                return string.Empty;
            }

            var action = enhance.BuffDebuffEnhanceType switch
            {
                1 => "Applied Stats Buff Tier Increased",
                2 => "Applied Stats Debuff Tier Increased",
                _ => "Buff/Debuff Tier Modified"
            };

            var parts = new List<string>
            {
                action,
                $"[Pot: {ResolveBuffTier(enhance.EnhanceEffectLevel)}]",
                $"[Rng.: {ResolveTarget(effect.TargetType)}]",
                $"[Dur: +{enhance.EnhanceDurationSec}s]",
                $"[Max Tier: {ResolveBuffTier(enhance.EnhanceEffectLevelMax)}]"
            };

            return string.Join(' ', parts);
        }

        private string BuildOveraccelGaugeChangeEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillOveraccelGaugeChangeEffectRaw> overaccelGaugeChanges)
        {
            if (!overaccelGaugeChanges.TryGetValue(effect.SkillEffectDetailId, out var change))
            {
                return string.Empty;
            }

            return $"Increases Overspeed Gauge [Pot: {change.PermilValue / 10.0:0.#}%] [Rng.: {ResolveTarget(effect.TargetType)}]";
        }

        private string BuildCostumeCountChangeEffect(
            SkillEffectRaw effect,
            Dictionary<long, SkillCostumeCountChangeEffectRaw> costumeCountChanges)
        {
            if (!costumeCountChanges.TryGetValue(effect.SkillEffectDetailId, out var change) || change.Value == 0)
            {
                return string.Empty;
            }

            return $"Gains {change.Value} extra uses of own Gear C. Ability";
        }

        private static string BuildFallbackEffect(SkillEffectRaw effect)
        {
            return $"{ResolveTarget(effect.TargetType)}: Effect {effect.SkillEffectType}";
        }

        private static string BuildCustomEffectDescription(
            SkillEffectRaw effect,
            Dictionary<long, SkillAdditionalEffectRaw> additionalEffects)
        {
            if (effect.SkillEffectType == 7 && additionalEffects.TryGetValue(effect.SkillEffectDetailId, out var additional))
            {
                return additional.SkillAdditionalType switch
                {
                    14 => $"Increase crit rate by {additional.MaxValue / 10.0:0.#}%",
                    15 => $"Deal {additional.MaxValue / 1000.0:0.#}x additional damage",
                    16 => $"Deal {additional.MaxValue} fixed additional damage",
                    _ => string.Empty
                };
            }

            return string.Empty;
        }

        private static string BuildTriggerConditionPrefix(
            SkillEffectRaw effect,
            Dictionary<long, SkillTriggerConditionHpRaw> triggerHpConditions)
        {
            var triggerType = effect.TriggerType;
            return triggerType switch
            {
                2 => "When hitting critical, ",
                3 => "When matching sigils are destroyed, ",
                4 => BuildHpCondition(effect, triggerHpConditions),
                7 => "When a debuff is on the target, ",
                8 => "When hitting target's weakness, ",
                13 => "With command gauge at max in attack stance, ",
                14 => "Against a single target, ",
                16 => "On first use, ",
                _ => string.Empty
            };
        }

        private static string BuildHpCondition(
            SkillEffectRaw effect,
            Dictionary<long, SkillTriggerConditionHpRaw> triggerHpConditions)
        {
            if (effect.TriggerConditionId == 0 ||
                !triggerHpConditions.TryGetValue(effect.TriggerConditionId, out var condition))
            {
                return string.Empty;
            }

            if (condition.MinPermil == 0)
            {
                return $"When HP is less than {condition.MaxPermil / 10.0:0.#}%, ";
            }

            if (condition.MaxPermil == 1000)
            {
                return $"When HP is greater than {condition.MinPermil / 10.0:0.#}%, ";
            }

            return string.Empty;
        }

        private static string FormatEffectWithCondition(string effectText, string conditionPrefix, bool isPrimaryEffect)
        {
            if (string.IsNullOrWhiteSpace(effectText))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(conditionPrefix))
            {
                return isPrimaryEffect ? effectText : $"Also, {effectText}";
            }

            var prefixed = $"{conditionPrefix}{effectText}";
            return isPrimaryEffect ? prefixed : $"Also, {prefixed}";
        }

        private static string ResolveBuffTier(int level)
        {
            if (level >= 0 && level < BuffDebuffTiers.Length)
            {
                return BuffDebuffTiers[level];
            }

            return BuffDebuffTiers[0];
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
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<List<T>>(stream, _jsonOptions) ?? new List<T>();
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to load data file {Path}", path);
                return new List<T>();
            }
        }

        private static int MakeUpgradeKey(int weaponId, int upgradeCount) => weaponId * 100 + upgradeCount;

        private static string ResolveEquipmentType(WeaponRaw weapon)
        {
            if (CustomWeaponTypes.TryGetValue(weapon.Id, out var custom))
            {
                return custom;
            }

            if (weapon.WeaponType >= 0 && weapon.WeaponType < EquipmentTypes.Length)
            {
                return EquipmentTypes[weapon.WeaponType];
            }

            return "Unknown";
        }

        private static string ResolveAttackType(int attackType)
        {
            if (attackType >= 0 && attackType < AttackTypes.Length)
            {
                return AttackTypes[attackType];
            }
            return "Unknown";
        }

        private static string ResolveElement(int elementType)
        {
            if (elementType >= 0 && elementType < ElementTypes.Length)
            {
                return ElementTypes[elementType];
            }
            return "Unknown";
        }

        private static string ResolveTarget(int targetType)
        {
            if (targetType >= 0 && targetType < TargetTypes.Length)
            {
                return TargetTypes[targetType];
            }
            return "Unknown Target";
        }

        private static string ResolveCommandSigil(int skillNotesSetId, Dictionary<long, SkillNotesSetRaw> notesSets)
        {
            if (skillNotesSetId == 0)
            {
                return string.Empty;
            }

            if (notesSets.TryGetValue(skillNotesSetId, out var set) && SkillNotesSigils.TryGetValue(set.SkillNotesId, out var sigil))
            {
                return sigil;
            }

            return string.Empty;
        }

        private static string StripMarkup(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return MarkupRegex.Replace(value, string.Empty).Trim();
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
                return _map.TryGetValue(id, out var value) ? value : $"#{id}";
            }
        }

        private static readonly Regex MarkupRegex = new("<.*?>", RegexOptions.Compiled);

        private static readonly string[] EquipmentTypes =
        {
            "Featured",
            "Grindable",
            "Event",
            "Limited"
        };

        private static readonly string[] AttackTypes =
        {
            "UNKNOWN",
            "Phys.",
            "Mag.",
            "Phys./Mag."
        };

        private static readonly string[] ElementTypes =
        {
            "UNKNOWN",
            "Non-Elemental",
            "Fire",
            "Ice",
            "Lightning",
            "Earth",
            "Water",
            "Wind"
        };

        private static readonly string[] TargetTypes =
        {
            "UNKNOWN",
            "Single Enemy",
            "All Enemies",
            "Single Ally",
            "All Allies",
            "Self",
            "Other Allies",
            "All Enemies + Allies"
        };

        private static readonly string[] SpecialGaugeTypes =
        {
            "UNKNOWN",
            "Limit Gauge",
            "Summon Gauge"
        };

        private static readonly string[] BuffDebuffTiers =
        {
            "UNKNOWN TIER",
            "Low",
            "Mid",
            "High",
            "Extra High",
            "Extreme"
        };

        private static readonly Dictionary<int, string> StatusEffectTypes = new()
        {
            { 1, "Poison" },
            { 3, "Silence" },
            { 4, "Darkness" },
            { 5, "Stun" },
            { 16, "Enfeeble" },
            { 17, "Stop" },
            { 18, "Fire Weakness" },
            { 19, "Ice Weakness" },
            { 20, "Lightning Weakness" },
            { 21, "Earth Weakness" },
            { 22, "Water Weakness" },
            { 23, "Wind Weakness" },
            { 27, "Single-Tgt. Phys. Dmg. Rcvd. Up" },
            { 28, "Single-Tgt. Mag. Dmg. Rcvd. Up" },
            { 29, "All-Tgt. Phys. Dmg. Rcvd. Up" },
            { 30, "All-Tgt. Mag. Dmg. Rcvd. Up" },
            { 43, "Torpor" }
        };

        private static readonly Dictionary<int, string> BuffDebuffTypes = new()
        {
            { 1, "PATK Up" },
            { 2, "PDEF Up" },
            { 3, "MATK Up" },
            { 4, "MDEF Up" },
            { 5, "PATK Down" },
            { 6, "PDEF Down" },
            { 7, "MATK Down" },
            { 8, "MDEF Down" },
            { 11, "Fire Resistance Up" },
            { 12, "Fire Resistance Down" },
            { 13, "Ice Resistance Up" },
            { 14, "Ice Resistance Down" },
            { 15, "Lightning Resistance Up" },
            { 16, "Lightning Resistance Down" },
            { 17, "Earth Resistance Up" },
            { 18, "Earth Resistance Down" },
            { 19, "Water Resistance Up" },
            { 20, "Water Resistance Down" },
            { 21, "Wind Resistance Up" },
            { 22, "Wind Resistance Down" },
            { 27, "Fire Damage Up" },
            { 28, "Fire Damage Down" },
            { 29, "Ice Damage Up" },
            { 30, "Ice Damage Down" },
            { 31, "Lightning Damage Up" },
            { 32, "Lightning Damage Down" },
            { 33, "Earth Damage Up" },
            { 34, "Earth Damage Down" },
            { 35, "Water Damage Up" },
            { 36, "Water Damage Down" },
            { 37, "Wind Damage Up" },
            { 38, "Wind Damage Down" }
        };

        private static readonly Dictionary<int, string> StatusChangeTypes = new()
        {
            { 2, "Provoke" },
            { 4, "Regen" },
            { 5, "Haste" },
            { 6, "Veil" },
            { 8, "Physical Resistance Increased" },
            { 9, "Magic Resistance Increased" },
            { 10, "HP Gain" },
            { 11, "Exploit Weakness" },
            { 12, "Amp. Phys. Abilities" },
            { 13, "Amp. Mag. Abilities" },
            { 23, "Amp. Healing Abilities" },
            { 24, "Phys. Damage Bonus" },
            { 25, "Mag. Damage Bonus" },
            { 26, "Fire Damage Bonus" },
            { 27, "Ice Damage Bonus" },
            { 28, "Lightning Damage Bonus" },
            { 29, "Earth Damage Bonus" },
            { 30, "Water Damage Bonus" },
            { 31, "Wind Damage Bonus" },
            { 34, "Phys. Weapon Boost" },
            { 35, "Mag. Weapon Boost" },
            { 36, "Fire Weapon Boost" },
            { 37, "Ice Weapon Boost" },
            { 38, "Lightning Weapon Boost" },
            { 39, "Earth Weapon Boost" },
            { 40, "Water Weapon Boost" },
            { 41, "Wind Weapon Boost" },
            { 44, "Phys. ATB Conservation Effect" },
            { 45, "Mag. ATB Conservation Effect" },
            { 52, "Enliven" }
        };

        private static readonly Dictionary<int, string> SkillNotesSigils = new()
        {
            { 1101, "◯ Circle" },
            { 2101, "△ Triangle" },
            { 3101, "✕ Cross" },
            { 4101, "◊ Diamond" }
        };

        private static readonly Dictionary<int, string> CustomWeaponTypes = new()
        {
            { 1013, "Guild" },
            { 2020, "Guild" },
            { 3014, "Guild" },
            { 4016, "Guild" },
            { 5027, "Guild" },
            { 6016, "Guild" },
            { 7011, "Guild" },
            { 8014, "Guild" },
            { 9016, "Guild" },
            { 20013, "Guild" },
            { 49013, "Guild" },
            { 50014, "Guild" },
            { 51013, "Guild" },
            { 52014, "Guild" },
            { 56011, "Guild" },
            { 4045, "Guild" },
            { 20037, "Guild" },
            { 1029, "Crossover" },
            { 1033, "Crossover" },
            { 1038, "Crossover" },
            { 1044, "Crossover" },
            { 1049, "Crossover" },
            { 1050, "Crossover" },
            { 2035, "Crossover" },
            { 2038, "Crossover" },
            { 3027, "Crossover" },
            { 3032, "Crossover" },
            { 3038, "Crossover" },
            { 3047, "Crossover" },
            { 3050, "Crossover" },
            { 3052, "Crossover" },
            { 4022, "Crossover" },
            { 4025, "Crossover" },
            { 4031, "Crossover" },
            { 4034, "Crossover" },
            { 4039, "Crossover" },
            { 4040, "Crossover" },
            { 4042, "Crossover" },
            { 4043, "Crossover" },
            { 6033, "Crossover" },
            { 6039, "Crossover" },
            { 20023, "Crossover" },
            { 20032, "Crossover" },
            { 49016, "Crossover" },
            { 49028, "Crossover" },
            { 49033, "Crossover" },
            { 49037, "Crossover" },
            { 49039, "Crossover" },
            { 50031, "Crossover" }
        };
    }
}
