using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public sealed class StagePointCalibrationFile
    {
        [JsonPropertyName("stages")]
        public Dictionary<string, List<CalibrationPoint>> Stages { get; set; } = new();

        [JsonPropertyName("bonuses")]
        public Dictionary<string, double> Bonuses { get; set; } = new();
    }

    public sealed class CalibrationPoint
    {
        [JsonPropertyName("percent")]
        public double Percent { get; set; }

        [JsonPropertyName("points")]
        public double Points { get; set; }
    }
}
