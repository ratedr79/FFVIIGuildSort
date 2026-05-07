using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class GearImageCatalog
    {
        public const string WeaponPlaceholderUrl = "/images/ui_icon_weapon.png";
        public const string OutfitPlaceholderUrl = "/images/ui_icon_outfit.png";

        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private static readonly string[] SupportedLocalImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

        private readonly Dictionary<string, string> _weaponImages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _costumeImages = new(StringComparer.OrdinalIgnoreCase);

        public GearImageCatalog(IWebHostEnvironment env)
        {
            _env = env;
            Load();
        }

        public string ResolveImageUrl(string name, string? character, string equipmentType)
        {
            var isCostume = string.Equals(equipmentType, "Costume", StringComparison.OrdinalIgnoreCase);
            var placeholder = isCostume ? OutfitPlaceholderUrl : WeaponPlaceholderUrl;
            var imageLookup = isCostume ? _costumeImages : _weaponImages;

            foreach (var key in EnumerateLookupKeys(name, character))
            {
                if (!imageLookup.TryGetValue(key, out var imageUrl) || string.IsNullOrWhiteSpace(imageUrl))
                {
                    continue;
                }

                if (IsUsableImageUrl(imageUrl))
                {
                    return imageUrl;
                }
            }

            return placeholder;
        }

        private void Load()
        {
            _weaponImages.Clear();
            _costumeImages.Clear();

            var path = Path.Combine(_env.ContentRootPath, "data", "gearImages.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<GearImageCatalogConfiguration>(json, _jsonOptions) ?? new GearImageCatalogConfiguration();

                LoadEntries(_weaponImages, config.Weapons);
                LoadEntries(_costumeImages, config.Costumes);
            }

            LoadLocalDirectoryEntries(_weaponImages, "images/weapons");
            LoadLocalDirectoryEntries(_costumeImages, "images/outfits");
        }

        private void LoadEntries(Dictionary<string, string> target, List<GearImageCatalogEntry>? entries)
        {
            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                var name = entry.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var imageUrl = NormalizeImageUrl(entry.ImageUrl ?? entry.Path);
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    continue;
                }

                target[BuildLookupKey(name, entry.Character)] = imageUrl;
            }
        }

        private void LoadLocalDirectoryEntries(Dictionary<string, string> target, string rootRelativeFolder)
        {
            var directoryPath = ResolveStaticAssetDirectory(rootRelativeFolder);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            foreach (var filePath in Directory
                .EnumerateFiles(directoryPath)
                .Where(IsSupportedLocalImageFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(filePath);
                var itemName = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                target[BuildLookupKey(itemName, null)] = BuildRootRelativePath(rootRelativeFolder, fileName);
            }
        }

        private string? ResolveStaticAssetDirectory(string rootRelativeFolder)
        {
            var relativePath = rootRelativeFolder.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var candidateBases = new[]
            {
                _env.WebRootPath,
                string.IsNullOrWhiteSpace(_env.ContentRootPath)
                    ? null
                    : Path.Combine(_env.ContentRootPath, "wwwroot")
            };

            foreach (var basePath in candidateBases
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var directoryPath = Path.Combine(basePath!, relativePath);
                if (Directory.Exists(directoryPath))
                {
                    return directoryPath;
                }
            }

            return null;
        }

        private static bool IsSupportedLocalImageFile(string path)
        {
            var extension = Path.GetExtension(path);
            return SupportedLocalImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildRootRelativePath(string rootRelativeFolder, string fileName)
        {
            var normalizedFolder = rootRelativeFolder.Trim().Trim('/').Replace('\\', '/');
            return $"/{normalizedFolder}/{fileName}";
        }

        private bool IsUsableImageUrl(string imageUrl)
        {
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps;
            }

            var rootRelativePath = imageUrl.StartsWith('/') ? imageUrl : $"/{imageUrl.TrimStart('/')}";
            return StaticAssetExists(rootRelativePath);
        }

        private bool StaticAssetExists(string rootRelativePath)
        {
            var relativePath = rootRelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var candidatePaths = new List<string>();

            if (!string.IsNullOrWhiteSpace(_env.WebRootPath))
            {
                candidatePaths.Add(Path.Combine(_env.WebRootPath, relativePath));
            }

            if (!string.IsNullOrWhiteSpace(_env.ContentRootPath))
            {
                candidatePaths.Add(Path.Combine(_env.ContentRootPath, "wwwroot", relativePath));
            }

            return candidatePaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Any(File.Exists);
        }

        private static IEnumerable<string> EnumerateLookupKeys(string name, string? character)
        {
            yield return BuildLookupKey(name, character);

            if (!string.IsNullOrWhiteSpace(character))
            {
                yield return BuildLookupKey(name, null);
            }
        }

        private static string BuildLookupKey(string? name, string? character)
        {
            return $"{NormalizeKey(name)}|{NormalizeKey(character)}";
        }

        private static string NormalizeKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().Replace('_', ' ');
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
                {
                    builder.Append(' ');
                    continue;
                }

                builder.Append(ch);
            }

            return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static string? NormalizeImageUrl(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var value = rawValue.Trim().Replace('\\', '/');
            if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri)
                && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
            {
                return value;
            }

            if (value.StartsWith("~/", StringComparison.Ordinal))
            {
                value = value[1..];
            }
            else if (value.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            {
                value = value["wwwroot".Length..];
            }

            if (!value.StartsWith('/'))
            {
                value = $"/{value.TrimStart('/')}";
            }

            return value;
        }

        private sealed class GearImageCatalogConfiguration
        {
            public List<GearImageCatalogEntry> Weapons { get; init; } = new();
            public List<GearImageCatalogEntry> Costumes { get; init; } = new();
        }

        private sealed class GearImageCatalogEntry
        {
            public string Name { get; init; } = string.Empty;
            public string? Character { get; init; }
            public string? Path { get; init; }
            public string? ImageUrl { get; init; }
            public string? SourceUrl { get; init; }
        }
    }
}
