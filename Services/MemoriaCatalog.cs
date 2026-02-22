using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class MemoriaCatalog
    {
        private readonly IWebHostEnvironment _env;
        private readonly Dictionary<string, MemoriaDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

        public MemoriaCatalog(IWebHostEnvironment env)
        {
            _env = env;
            Load();
        }

        private void Load()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "memoria.json");
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<MemoriaConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cfg?.Memoria == null)
            {
                return;
            }

            foreach (var m in cfg.Memoria)
            {
                if (string.IsNullOrWhiteSpace(m.Name))
                {
                    continue;
                }

                _byName[m.Name.Trim()] = m;
            }
        }

        public bool TryGetMemoria(string name, out MemoriaDefinition def) => _byName.TryGetValue(name, out def!);
    }
}
