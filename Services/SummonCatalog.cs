using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class SummonCatalog
    {
        private readonly IWebHostEnvironment _env;
        private readonly Dictionary<string, SummonDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

        public SummonCatalog(IWebHostEnvironment env)
        {
            _env = env;
            Load();
        }

        private void Load()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "summons.json");
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<SummonsConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cfg?.Summons == null)
            {
                return;
            }

            foreach (var s in cfg.Summons)
            {
                if (string.IsNullOrWhiteSpace(s.Name))
                {
                    continue;
                }

                _byName[s.Name.Trim()] = s;
            }
        }

        public bool TryGetSummon(string name, out SummonDefinition def) => _byName.TryGetValue(name, out def!);

        public IReadOnlyList<SummonDefinition> GetAll() => _byName.Values.ToList();
    }
}
