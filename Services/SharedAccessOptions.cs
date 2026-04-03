namespace FFVIIEverCrisisAnalyzer.Services;

public sealed class SharedAccessOptions
{
    public const string SectionName = "SharedAccess";

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordVersion { get; set; } = "v1";

    public string CookieName { get; set; } = "LeadershipUnlock";

    public int UnlockDurationHours { get; set; } = 12;

    public List<string> ProtectedPages { get; set; } = new();
}
