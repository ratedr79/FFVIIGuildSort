using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class TeamTemplateCatalog
    {
        private readonly IWebHostEnvironment _env;
        private List<TeamTemplate> _templates = new();
        private List<List<string>> _mutuallyExclusiveCharacterGroups = new();

        public TeamTemplateCatalog(IWebHostEnvironment env)
        {
            _env = env;
            Load();
        }

        private void Load()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "teamTemplates.json");
            if (!File.Exists(path))
            {
                _templates = new List<TeamTemplate>
                {
                    new TeamTemplate { Name = "DPS/Support/Healer", Roles = new List<string> { "DPS", "Support", "Healer" }, Enabled = true, Priority = 1 },
                    new TeamTemplate { Name = "DPS/Tank/Healer", Roles = new List<string> { "DPS", "Tank", "Healer" }, Enabled = true, Priority = 2 },
                    new TeamTemplate { Name = "DPS/Support/Tank", Roles = new List<string> { "DPS", "Support", "Tank" }, Enabled = true, Priority = 3 },
                    new TeamTemplate { Name = "DPS/DPS/Healer", Roles = new List<string> { "DPS", "DPS", "Healer" }, Enabled = true, Priority = 4 }
                };
                _mutuallyExclusiveCharacterGroups = new List<List<string>>
                {
                    new() { "Sephiroth", "Sephiroth (Original)" }
                };
                return;
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<TeamTemplateConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cfg?.ValidTeamTemplates != null)
            {
                _templates = cfg.ValidTeamTemplates;
            }

            _mutuallyExclusiveCharacterGroups = cfg?.MutuallyExclusiveCharacterGroups?
                .Where(group => group != null && group.Count > 1)
                .Select(group => group.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                .Where(group => group.Count > 1)
                .ToList()
                ?? new List<List<string>>();
        }

        public List<TeamTemplate> GetAllTemplates()
        {
            return _templates.ToList();
        }

        public List<TeamTemplate> GetEnabledTemplates()
        {
            return _templates.Where(t => t.Enabled).OrderBy(t => t.Priority).ToList();
        }

        public List<List<string>> GetMutuallyExclusiveCharacterGroups()
        {
            return _mutuallyExclusiveCharacterGroups
                .Select(group => group.ToList())
                .ToList();
        }
    }
}
