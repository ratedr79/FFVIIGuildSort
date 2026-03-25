using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;
using Microsoft.Extensions.Logging;

namespace FFVIIEverCrisisAnalyzer.Services
{
    /// <summary>
    /// Estimates guild-battle points for a given stage and HP percentage
    /// using linear interpolation between configurable calibration points.
    /// Loaded once from data/stagePointCalibration.json on construction.
    /// </summary>
    public sealed class StagePointEstimator
    {
        private readonly Dictionary<StageId, List<CalibrationPoint>> _calibration;
        private readonly Dictionary<StageId, double> _bonuses;
        private readonly ILogger<StagePointEstimator> _logger;

        // Damage thresholds that award bonuses (in terms of remaining HP)
        private static readonly double[] BonusHpThresholds = { 75, 50, 25, 0 };

        public StagePointEstimator(ILogger<StagePointEstimator> logger)
        {
            _logger = logger;
            (_calibration, _bonuses) = LoadCalibration();
        }

        /// <summary>
        /// Estimate points for a single stage at the given HP percentage (0–100).
        /// </summary>
        public double GetPoints(StageId stage, double percent)
        {
            percent = Math.Clamp(percent, 0, 100);

            if (!_calibration.TryGetValue(stage, out var points) || points.Count == 0)
                return 0;

            // Exact match
            var exact = points.FirstOrDefault(p => Math.Abs(p.Percent - percent) < 0.0001);
            if (exact != null)
                return exact.Points;

            // Find surrounding calibration points for interpolation
            CalibrationPoint? lower = null;
            CalibrationPoint? upper = null;

            foreach (var cp in points)
            {
                if (cp.Percent <= percent)
                {
                    if (lower == null || cp.Percent > lower.Percent)
                        lower = cp;
                }
                if (cp.Percent >= percent)
                {
                    if (upper == null || cp.Percent < upper.Percent)
                        upper = cp;
                }
            }

            if (lower != null && upper != null && Math.Abs(upper.Percent - lower.Percent) > 0.0001)
            {
                double t = (percent - lower.Percent) / (upper.Percent - lower.Percent);
                return lower.Points + t * (upper.Points - lower.Points);
            }

            // Edge: only one bound available (extrapolation)
            if (lower != null) return lower.Points;
            if (upper != null) return upper.Points;

            return 0;
        }

        /// <summary>
        /// Estimate points for all stages at the given HP percentages.
        /// </summary>
        public Dictionary<StageId, double> GetPointsAll(Dictionary<StageId, double> hpByStage)
        {
            var result = new Dictionary<StageId, double>();
            foreach (var kvp in hpByStage)
            {
                result[kvp.Key] = GetPoints(kvp.Key, kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// Get the calibration points for a specific stage (for UI display).
        /// </summary>
        public List<CalibrationPoint> GetCalibration(StageId stage)
        {
            return _calibration.TryGetValue(stage, out var points)
                ? points.ToList()
                : new List<CalibrationPoint>();
        }

        /// <summary>
        /// Calculate bonus points earned when HP drops from hpBefore to hpAfter.
        /// Bonuses are awarded for each 25% damage threshold crossed (remaining HP passes 75%, 50%, 25%, or 0%).
        /// </summary>
        public double GetBonusPoints(StageId stage, double hpBefore, double hpAfter)
        {
            if (!_bonuses.TryGetValue(stage, out var bonusPerThreshold) || bonusPerThreshold <= 0)
                return 0;

            int count = 0;
            foreach (var threshold in BonusHpThresholds)
            {
                // Threshold crossed if HP was above it before and at or below it after
                if (hpBefore > threshold + 0.005 && hpAfter <= threshold + 0.005)
                    count++;
            }

            return count * bonusPerThreshold;
        }

        /// <summary>
        /// Get the bonus amount per threshold for a stage.
        /// </summary>
        public double GetBonusPerThreshold(StageId stage)
        {
            return _bonuses.GetValueOrDefault(stage, 0);
        }

        /// <summary>
        /// Check whether calibration data was loaded successfully.
        /// </summary>
        public bool HasCalibration => _calibration.Count > 0;

        private (Dictionary<StageId, List<CalibrationPoint>>, Dictionary<StageId, double>) LoadCalibration()
        {
            var calibration = new Dictionary<StageId, List<CalibrationPoint>>();
            var bonuses = new Dictionary<StageId, double>();
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "stagePointCalibration.json");
                if (!File.Exists(path))
                {
                    _logger.LogWarning("Stage point calibration file not found at {Path}; using empty calibration.", path);
                    return (calibration, bonuses);
                }

                var json = File.ReadAllText(path);
                var file = JsonSerializer.Deserialize<StagePointCalibrationFile>(json);
                if (file?.Stages == null)
                {
                    _logger.LogWarning("Stage point calibration file is empty or malformed.");
                    return (calibration, bonuses);
                }

                foreach (var kvp in file.Stages)
                {
                    if (Enum.TryParse<StageId>(kvp.Key, ignoreCase: true, out var stageId))
                    {
                        var sorted = kvp.Value
                            .Where(cp => cp.Points >= 0)
                            .OrderBy(cp => cp.Percent)
                            .ToList();

                        if (sorted.Count > 0)
                            calibration[stageId] = sorted;
                    }
                    else
                    {
                        _logger.LogWarning("Unknown stage key '{Key}' in calibration file; skipping.", kvp.Key);
                    }
                }

                if (file.Bonuses != null)
                {
                    foreach (var kvp in file.Bonuses)
                    {
                        if (Enum.TryParse<StageId>(kvp.Key, ignoreCase: true, out var stageId))
                            bonuses[stageId] = kvp.Value;
                    }
                    _logger.LogInformation("Loaded bonus values for {Count} stages.", bonuses.Count);
                }

                _logger.LogInformation("Loaded stage point calibration for {Count} stages.", calibration.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load stage point calibration; using empty calibration.");
            }

            return (calibration, bonuses);
        }
    }
}
