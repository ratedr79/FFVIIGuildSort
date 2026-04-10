using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class SupportTeamPresetCatalog
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private List<SupportTeamPreset> _presets = new();

        public SupportTeamPresetCatalog(IWebHostEnvironment env)
        {
            _env = env;
            Load();
        }

        private void Load()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "supportTeamPresets.json");
            if (!File.Exists(path))
            {
                _presets = new();
                return;
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<SupportTeamPresetConfiguration>(json, _jsonOptions);
            _presets = config?.Presets?
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new SupportTeamPreset
                {
                    Name = p.Name.Trim(),
                    Effects = (p.Effects ?? new List<string>())
                        .Where(effect => !string.IsNullOrWhiteSpace(effect))
                        .Select(effect => effect.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .Where(p => p.Effects.Count > 0)
                .ToList()
                ?? new List<SupportTeamPreset>();
        }

        public List<SupportTeamPreset> GetPresets()
        {
            return _presets
                .Select(p => new SupportTeamPreset
                {
                    Name = p.Name,
                    Effects = p.Effects.ToList()
                })
                .ToList();
        }
    }
}
