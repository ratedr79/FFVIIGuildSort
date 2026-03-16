namespace FFVIIEverCrisisAnalyzer.Models
{
    public class TeamTemplateConfiguration
    {
        public List<TeamTemplate> ValidTeamTemplates { get; set; } = new();
    }

    public class TeamTemplate
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; }
    }
}
