using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

/// <summary>
/// Lists whatever is in the downloads folder (config Downloads:Path, data/downloads under the site by default)
/// for anyone to download. Files in subfolders are grouped by folder.
/// </summary>
public class ReawakenedDownloadsModel : PageModel
{
    public sealed record DownloadFile(string RelativePath, string Folder, string Name, long Size, DateTime ModifiedUtc, string FullPath);

    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "Thumbs.db" };

    private readonly string _folder;

    public ReawakenedDownloadsModel(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _folder = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            configuration["Downloads:Path"] ?? Path.Combine("data", "downloads")));
    }

    public IReadOnlyList<IGrouping<string, DownloadFile>> Groups { get; private set; } = Array.Empty<IGrouping<string, DownloadFile>>();

    public void OnGet()
    {
        Groups = ListFiles()
            .OrderByDescending(f => f.ModifiedUtc)
            .GroupBy(f => f.Folder)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IActionResult OnGetDownload(string? file)
    {
        // Only a name from the listing is served, so a crafted path can't reach anything outside the folder.
        var match = ListFiles().FirstOrDefault(f => string.Equals(f.RelativePath, file, StringComparison.Ordinal));
        if (match == null)
        {
            return NotFound();
        }

        return new PhysicalFileResult(match.FullPath, "application/octet-stream") { FileDownloadName = match.Name, EnableRangeProcessing = true };
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    private List<DownloadFile> ListFiles()
    {
        if (!Directory.Exists(_folder))
        {
            return new List<DownloadFile>();
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new DirectoryInfo(_folder).EnumerateFiles("*", options)
            .Where(f => !f.Name.StartsWith('.') && !IgnoredNames.Contains(f.Name))
            .Select(f =>
            {
                var relative = Path.GetRelativePath(_folder, f.FullName).Replace(Path.DirectorySeparatorChar, '/');
                var slash = relative.LastIndexOf('/');
                return new DownloadFile(relative, slash < 0 ? string.Empty : relative[..slash], f.Name, f.Length, f.LastWriteTimeUtc, f.FullName);
            })
            .ToList();
    }
}
