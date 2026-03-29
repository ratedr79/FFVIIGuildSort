using System;
using System.Collections.Generic;
using System.Linq;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class WeaponStatCalculator
    {
        private readonly Dictionary<int, Dictionary<int, WeaponLevelRaw>> _levelsByGroup;
        private readonly Dictionary<(int GroupId, int UpgradeType), Dictionary<int, WeaponUpgradeParameterRaw>> _upgradeParameters;
        private readonly IReadOnlyDictionary<int, WeaponRarityRaw> _weaponRarities;

        private const int StatScale = 1000;

        public WeaponStatCalculator(
            IEnumerable<WeaponLevelRaw> weaponLevels,
            IEnumerable<WeaponUpgradeParameterRaw> weaponUpgradeParameters,
            IReadOnlyDictionary<int, WeaponRarityRaw> weaponRarities)
        {
            _levelsByGroup = weaponLevels
                .GroupBy(l => l.WeaponLevelGroupId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(l => l.Level));

            _upgradeParameters = weaponUpgradeParameters
                .GroupBy(p => (p.WeaponUpgradeParameterGroupId, p.WeaponUpgradeType))
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(p => p.UpgradeCount));

            _weaponRarities = weaponRarities;
        }

        public WeaponStatResult ComputeStats(
            WeaponRaw weapon,
            int targetLevel,
            int targetUpgradeCount,
            int? overrideUpgradeType = null)
        {
            if (!_weaponRarities.TryGetValue(weapon.Id, out var rarity))
            {
                return WeaponStatResult.Empty;
            }

            if (!_levelsByGroup.TryGetValue(weapon.WeaponLevelGroupId, out var levels) ||
                !levels.TryGetValue(targetLevel, out var levelRow))
            {
                return WeaponStatResult.Empty;
            }

            var upgradeType = overrideUpgradeType ?? 1;
            var addCoefficients = GetUpgradeCoefficients(
                weapon.WeaponUpgradeParameterGroupId,
                upgradeType,
                targetUpgradeCount);

            var patk = CalculateStat(
                rarity.BasePhysicalAttack,
                rarity.GrowthBasePhysicalAttack,
                levelRow.PhysicalAttackCoefficient,
                addCoefficients.PhysicalAttackAddCoefficient);

            var matk = CalculateStat(
                rarity.BaseMagicalAttack,
                rarity.GrowthBaseMagicalAttack,
                levelRow.MagicalCoefficient,
                addCoefficients.MagicalAddCoefficient);

            var heal = CalculateStat(
                rarity.BaseHealingPower,
                rarity.GrowthBaseHealingPower,
                levelRow.HealingPowerCoefficient,
                addCoefficients.HealingPowerAddCoefficient);

            return new WeaponStatResult(patk, matk, heal);
        }

        private WeaponUpgradeParameterRaw GetUpgradeCoefficients(int groupId, int upgradeType, int targetUpgradeCount)
        {
            if (!_upgradeParameters.TryGetValue((groupId, upgradeType), out var byCount) || byCount.Count == 0)
            {
                return DefaultUpgradeParameters;
            }

            if (targetUpgradeCount <= 0 && byCount.TryGetValue(0, out var zeroEntry))
            {
                return zeroEntry;
            }

            if (byCount.TryGetValue(targetUpgradeCount, out var exact))
            {
                return exact;
            }

            // Fallback to the highest available upgrade count below the target.
            var fallback = byCount
                .Where(kvp => kvp.Key <= targetUpgradeCount)
                .OrderByDescending(kvp => kvp.Key)
                .Select(kvp => kvp.Value)
                .FirstOrDefault();

            return fallback ?? DefaultUpgradeParameters;
        }

        private static int CalculateStat(int baseValue, int growthBaseValue, int levelCoefficient, int addCoefficient)
        {
            var growthContribution = Math.Floor(growthBaseValue * levelCoefficient / (double)StatScale);
            var baseStat = baseValue + (int)growthContribution;
            var multiplier = (StatScale + addCoefficient) / (double)StatScale;
            return (int)Math.Floor(baseStat * multiplier);
        }

        private static readonly WeaponUpgradeParameterRaw DefaultUpgradeParameters = new()
        {
            PhysicalAttackAddCoefficient = 0,
            MagicalAddCoefficient = 0,
            HealingPowerAddCoefficient = 0
        };
    }

    public readonly record struct WeaponStatResult(int PhysicalAttack, int MagicalAttack, int HealingPower)
    {
        public static WeaponStatResult Empty { get; } = new(0, 0, 0);
    }
}
