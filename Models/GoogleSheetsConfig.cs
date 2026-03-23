namespace FFVIIEverCrisisAnalyzer.Models
{
    public class GoogleSheetsConfig
    {
        public List<SheetDefinition> GuildBattleSheets { get; set; } = new();
        public List<SheetDefinition> SurveySheets { get; set; } = new();
    }

    public class SheetDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
