using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class EnemyAbilityCatalog
    {
        private readonly IWebHostEnvironment _env;
        private readonly Dictionary<string, EnemyAbilityDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

        public EnemyAbilityCatalog(IWebHostEnvironment env)
        {
            _env = env;
            Load();
        }

        private void Load()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "enemyAbilities.json");
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<EnemyAbilitiesConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cfg?.EnemyAbilities == null)
            {
                return;
            }

            foreach (var a in cfg.EnemyAbilities)
            {
                if (string.IsNullOrWhiteSpace(a.Name))
                {
                    continue;
                }

                _byName[a.Name.Trim()] = a;
            }
        }

        public bool TryGetEnemyAbility(string name, out EnemyAbilityDefinition def) => _byName.TryGetValue(name, out def!);
    }
}
