using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FFVIIEverCrisisAnalyzer.Services
{
    /// <summary>
    /// The ECR (Reawakened) Rebalancing changes as rows to lay over the UnknownX7 MasterData
    /// (data/ecr/ecr-overlay.json, written by tools/build_ecr_overlay.py from the launcher's recipe).
    /// When the launcher's recipe has changed since the overlay was built and the launcher's Python is
    /// there, the overlay is rebuilt before use, so the ECR Gear Search follows the recipe on its own.
    /// </summary>
    public sealed class EcrOverlayStore
    {
        public sealed record Change(string Id, string Group, string Name, string Text, IReadOnlyList<string> GearIds);

        private sealed record Table(IReadOnlyList<string> Keys, IReadOnlyList<JsonObject> Replace, IReadOnlyList<JsonObject> Add);

        private readonly ILogger<EcrOverlayStore> _logger;
        private readonly string _overlayPath;
        private readonly string _builderPath;
        private readonly string _ecrRoot;
        private readonly object _sync = new();
        private Dictionary<string, Table> _tables = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _checkedRecipeWriteUtc;

        public EcrOverlayStore(ILogger<EcrOverlayStore> logger, IWebHostEnvironment environment, IConfiguration configuration)
        {
            _logger = logger;
            _overlayPath = Path.Combine(environment.ContentRootPath, "data", "ecr", "ecr-overlay.json");
            _builderPath = Path.Combine(environment.ContentRootPath, "tools", "build_ecr_overlay.py");
            _ecrRoot = configuration["Ecr:Root"] ?? @"C:\FF7EC\ECR";
            Load();
        }

        public bool Available { get; private set; }
        public string? GeneratedUtc { get; private set; }
        public string? RecipeSha256 { get; private set; }
        public string? LastError { get; private set; }
        public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<Change> Changes { get; private set; } = Array.Empty<Change>();
        public IReadOnlyDictionary<long, string> Texts { get; private set; } = new Dictionary<long, string>();

        /// <summary>Changes by Gear Search item id (a weapon id, or "costume-{id}").</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<Change>> ChangesByGearId { get; private set; } =
            new Dictionary<string, IReadOnlyList<Change>>();

        private string RecipePath => Path.Combine(_ecrRoot, "engine", "data", "masterdata-patches.json");
        private string PythonPath => Path.Combine(_ecrRoot, "python", "python.exe");

        /// <summary>
        /// Rebuilds the overlay when the launcher's recipe differs from the one it was built from.
        /// Returns true when the overlay changed (the data service should then reload).
        /// </summary>
        public bool RefreshFromRecipe()
        {
            lock (_sync)
            {
                if (!File.Exists(RecipePath))
                    return false;
                var written = File.GetLastWriteTimeUtc(RecipePath);
                if (written == _checkedRecipeWriteUtc)
                    return false;
                _checkedRecipeWriteUtc = written;

                string sha;
                try
                {
                    sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(RecipePath))).ToLowerInvariant();
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not read the ECR recipe at {Path}", RecipePath);
                    _checkedRecipeWriteUtc = default;
                    return false;
                }
                if (sha == RecipeSha256)
                    return false;
                if (!File.Exists(PythonPath) || !File.Exists(_builderPath))
                {
                    LastError = "The ECR launcher's recipe has changed, but its Python was not found to rebuild the overlay.";
                    return false;
                }

                var start = new ProcessStartInfo(PythonPath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (var arg in new[] { "-I", _builderPath, _ecrRoot })
                    start.ArgumentList.Add(arg);
                try
                {
                    using var process = Process.Start(start)!;
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120_000))
                    {
                        process.Kill(true);
                        LastError = "Rebuilding the ECR overlay timed out.";
                        return false;
                    }
                    if (process.ExitCode != 0)
                    {
                        LastError = "Rebuilding the ECR overlay failed: " + stderr.Result.Trim().Split('\n').LastOrDefault();
                        _logger.LogWarning("ECR overlay build failed: {Error}", stderr.Result);
                        return false;
                    }
                    _logger.LogInformation("ECR overlay rebuilt: {Output}", stdout.Result.Trim());
                }
                catch (Exception ex)
                {
                    LastError = "Rebuilding the ECR overlay failed: " + ex.Message;
                    _logger.LogWarning(ex, "ECR overlay build failed");
                    return false;
                }

                LastError = null;
                Load();
                return true;
            }
        }

        /// <summary>
        /// Applies the overlay to one MasterData file's rows: replaces rows matching on the table's key
        /// fields and appends added rows. Tables the overlay doesn't touch come back unchanged.
        /// </summary>
        public JsonArray Apply(string fileName, JsonArray rows)
        {
            if (!_tables.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out var table))
                return rows;

            if (table.Replace.Count > 0)
            {
                var replacements = table.Replace.ToDictionary(r => KeyOf(r, table.Keys), r => r);
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i] is JsonObject row && replacements.TryGetValue(KeyOf(row, table.Keys), out var replacement))
                        rows[i] = replacement.DeepClone();
                }
            }
            foreach (var added in table.Add)
                rows.Add(added.DeepClone());
            return rows;
        }

        private static string KeyOf(JsonObject row, IReadOnlyList<string> keys) =>
            string.Join("|", keys.Select(k => row[k]?.ToJsonString() ?? "null"));

        private void Load()
        {
            if (!File.Exists(_overlayPath))
            {
                Available = false;
                return;
            }
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(_overlayPath))!.AsObject();
                var tables = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, node) in root["tables"]!.AsObject())
                {
                    var entry = node!.AsObject();
                    tables[name] = new Table(
                        entry["keys"]?.AsArray().Select(k => k!.GetValue<string>()).ToList() ?? new List<string>(),
                        entry["replace"]?.AsArray().Select(r => r!.AsObject()).ToList() ?? new List<JsonObject>(),
                        entry["add"]?.AsArray().Select(r => r!.AsObject()).ToList() ?? new List<JsonObject>());
                }
                var changes = root["changes"]!.AsArray().Select(c => new Change(
                    (string)c!["id"]!, (string)c["group"]!, (string)c["name"]!, (string)c["text"]!,
                    c["gearIds"]!.AsArray().Select(g => (string)g!).ToList())).ToList();

                _tables = tables;
                Changes = changes;
                ChangesByGearId = changes
                    .SelectMany(c => c.GearIds.Select(id => (id, c)))
                    .GroupBy(x => x.id)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<Change>)g.Select(x => x.c).ToList());
                Texts = root["texts"]!.AsObject().ToDictionary(t => long.Parse(t.Key), t => (string)t.Value!);
                Warnings = root["warnings"]?.AsArray().Select(w => (string)w!).ToList() ?? new List<string>();
                GeneratedUtc = (string?)root["generatedUtc"];
                RecipeSha256 = (string?)root["recipeSha256"];
                Available = true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or NullReferenceException)
            {
                _logger.LogError(ex, "Could not read the ECR overlay at {Path}", _overlayPath);
                LastError = "The ECR overlay file could not be read.";
                Available = false;
            }
        }
    }
}
