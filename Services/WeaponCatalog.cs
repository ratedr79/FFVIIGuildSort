using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class WeaponCatalog
    {
        private readonly Dictionary<string, WeaponInfo> _byWeaponName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WeaponInfo> _byWeaponNameNormalized = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CostumeInfo> _byCostumeName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<CostumeInfo>> _costumesByCharacter = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, WeaponInfo> ByWeaponName => _byWeaponName;
        public IReadOnlyDictionary<string, CostumeInfo> ByCostumeName => _byCostumeName;

        public WeaponCatalog(IWebHostEnvironment env)
        {
            var dataPath = Path.Combine(env.ContentRootPath, "data", "weaponData.tsv");
            if (File.Exists(dataPath))
            {
                using var stream = File.OpenRead(dataPath);
                using var reader = new StreamReader(stream);

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = "\t",
                    HasHeaderRecord = true,
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    BadDataFound = null,
                };

                using var csv = new CsvReader(reader, config);
                csv.Read();
                csv.ReadHeader();

                while (csv.Read())
                {
                    var name = (csv.GetField("Name") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var character = (csv.GetField("Character") ?? string.Empty).Trim();
                    var gachaType = (csv.GetField("GachaType") ?? string.Empty).Trim();
                    var abilityElement = (csv.GetField("Ability Element") ?? string.Empty).Trim();
                    var abilityType = (csv.GetField("Ability Type") ?? string.Empty).Trim();
                    var abilityRange = (csv.GetField("Ability Range") ?? string.Empty).Trim();
                    var abilityPotRaw = (csv.GetField("Ability Pot. %") ?? string.Empty).Trim();

                    double? abilityPotPercent = null;
                    if (double.TryParse(abilityPotRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var potParsed))
                    {
                        abilityPotPercent = potParsed;
                    }

                    // Some weapons have additional "Multiply Damage" effects that add to ability potency.
                    // We treat the corresponding *_Pot / *_PotMax as an additive percentage.
                    double multiplyDamageBonusPercent = 0;
                    for (int i = 0; i <= 3; i++)
                    {
                        var effect = (csv.GetField($"Effect{i}") ?? string.Empty).Trim();
                        if (!effect.Equals("Multiply Damage", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var potRaw = (csv.GetField($"Effect{i}_Pot") ?? string.Empty).Trim();
                        var potMaxRaw = (csv.GetField($"Effect{i}_PotMax") ?? string.Empty).Trim();

                        var chosenRaw = !string.IsNullOrWhiteSpace(potMaxRaw) ? potMaxRaw : potRaw;
                        if (double.TryParse(chosenRaw.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out var bonusParsed))
                        {
                            multiplyDamageBonusPercent += bonusParsed;
                        }
                    }

                    // Collect effect columns into a searchable blob.
                    var effectFields = new[]
                    {
                        "Effect0", "Effect0_Type", "Effect1", "Effect1_Type", "Effect2", "Effect2_Type", "Effect3", "Effect3_Type"
                    };

                    var effectText = string.Join(" | ", effectFields
                        .Select(f => (csv.GetField(f) ?? string.Empty).Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                    );

                    // Include effect metadata (potency + count) so we can score limited-use effects like Amp abilities.
                    var metaParts = new List<string>();
                    for (int i = 0; i <= 3; i++)
                    {
                        var eff = (csv.GetField($"Effect{i}") ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(eff))
                        {
                            continue;
                        }

                        var pot = (csv.GetField($"Effect{i}_Pot") ?? string.Empty).Trim();
                        var potMax = (csv.GetField($"Effect{i}_PotMax") ?? string.Empty).Trim();
                        var count = (csv.GetField($"Effect{i}_EffectCount") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(pot))
                        {
                            metaParts.Add($"{eff} Pot={pot}");
                        }
                        if (!string.IsNullOrWhiteSpace(potMax))
                        {
                            metaParts.Add($"{eff} PotMax={potMax}");
                        }
                        if (!string.IsNullOrWhiteSpace(count))
                        {
                            metaParts.Add($"{eff} Count={count}");
                        }
                    }

                    if (metaParts.Count > 0)
                    {
                        effectText = string.Join(" | ", new[] { effectText, string.Join(" | ", metaParts) }
                            .Where(s => !string.IsNullOrWhiteSpace(s)));
                    }

                    AddWeaponOrCostume(name, character, gachaType, abilityElement, abilityType, abilityRange, abilityPotPercent, multiplyDamageBonusPercent, effectText);
                }
            }

            LoadFallbackJson(env);
        }

        private void AddWeaponOrCostume(
            string name,
            string character,
            string gachaType,
            string abilityElement,
            string abilityType,
            string abilityRange,
            double? abilityPotPercent,
            double multiplyDamageBonusPercent,
            string effectText)
        {
            _byWeaponName[name] = new WeaponInfo
            {
                Name = name,
                Character = character,
                GachaType = gachaType,
                IsUltimate = gachaType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase),
                AbilityElement = abilityElement,
                AbilityType = abilityType,
                AbilityRange = abilityRange,
                AbilityPotPercentAtOb10 = abilityPotPercent,
                MultiplyDamageBonusPercent = multiplyDamageBonusPercent,
                EffectTextBlob = effectText
            };

            var normalizedName = NormalizeKey(name);
            if (!_byWeaponNameNormalized.ContainsKey(normalizedName))
            {
                _byWeaponNameNormalized[normalizedName] = _byWeaponName[name];
            }

            if (gachaType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
            {
                var costume = new CostumeInfo
                {
                    Name = name,
                    Character = character,
                    AbilityElement = abilityElement,
                    AbilityType = abilityType,
                    AbilityRange = abilityRange,
                    EffectTextBlob = effectText,
                };

                _byCostumeName[name] = costume;

                if (!_costumesByCharacter.TryGetValue(character, out var list))
                {
                    list = new List<CostumeInfo>();
                    _costumesByCharacter[character] = list;
                }

                list.Add(costume);
            }
        }

        private void LoadFallbackJson(IWebHostEnvironment env)
        {
            var path = Path.Combine(env.ContentRootPath, "data", "weaponFallback.json");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var doc = JsonSerializer.Deserialize<FallbackDoc>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (doc == null)
                {
                    return;
                }

                if (doc.Weapons != null)
                {
                    foreach (var w in doc.Weapons)
                    {
                        if (string.IsNullOrWhiteSpace(w.Name))
                        {
                            continue;
                        }

                        if (_byWeaponName.ContainsKey(w.Name))
                        {
                            continue; // TSV wins
                        }

                        var effectText = BuildFallbackEffectText(w);
                        AddWeaponOrCostume(
                            w.Name.Trim(),
                            (w.Character ?? string.Empty).Trim(),
                            (w.GachaType ?? string.Empty).Trim(),
                            (w.AbilityElement ?? string.Empty).Trim(),
                            (w.AbilityType ?? string.Empty).Trim(),
                            (w.AbilityRange ?? string.Empty).Trim(),
                            w.AbilityPotPercentAtOb10,
                            w.MultiplyDamageBonusPercent ?? 0,
                            effectText);
                    }
                }

                if (doc.Costumes != null)
                {
                    foreach (var c in doc.Costumes)
                    {
                        if (string.IsNullOrWhiteSpace(c.Name))
                        {
                            continue;
                        }

                        if (_byCostumeName.ContainsKey(c.Name))
                        {
                            continue; // TSV wins
                        }

                        var effectText = BuildFallbackEffectText(c);
                        AddWeaponOrCostume(
                            c.Name.Trim(),
                            (c.Character ?? string.Empty).Trim(),
                            "Costume",
                            (c.AbilityElement ?? string.Empty).Trim(),
                            (c.AbilityType ?? string.Empty).Trim(),
                            (c.AbilityRange ?? string.Empty).Trim(),
                            null,
                            0,
                            effectText);
                    }
                }
            }
            catch
            {
                // Swallow: fallback file is optional.
            }
        }

        private static string BuildFallbackEffectText(FallbackEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.EffectTextBlob))
            {
                return entry.EffectTextBlob.Trim();
            }

            if (string.IsNullOrWhiteSpace(entry.Effect))
            {
                return string.Empty;
            }

            var parts = new List<string> { entry.Effect.Trim() };
            if (!string.IsNullOrWhiteSpace(entry.EffectType)) parts.Add(entry.EffectType.Trim());
            if (!string.IsNullOrWhiteSpace(entry.EffectPot)) parts.Add($"{entry.Effect.Trim()} Pot={entry.EffectPot.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.EffectPotMax)) parts.Add($"{entry.Effect.Trim()} PotMax={entry.EffectPotMax.Trim()}");
            if (!string.IsNullOrWhiteSpace(entry.EffectCount)) parts.Add($"{entry.Effect.Trim()} Count={entry.EffectCount.Trim()}");

            return string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private sealed class FallbackDoc
        {
            public List<FallbackWeaponEntry>? Weapons { get; set; }
            public List<FallbackCostumeEntry>? Costumes { get; set; }
        }

        private abstract class FallbackEntry
        {
            public string Name { get; set; } = string.Empty;
            public string? Character { get; set; }
            public string? AbilityElement { get; set; }
            public string? AbilityType { get; set; }
            public string? AbilityRange { get; set; }
            public string? EffectTextBlob { get; set; }

            public string? Effect { get; set; }
            public string? EffectType { get; set; }
            public string? EffectPot { get; set; }
            public string? EffectPotMax { get; set; }
            public string? EffectCount { get; set; }
        }

        private sealed class FallbackWeaponEntry : FallbackEntry
        {
            public string? GachaType { get; set; }
            public double? AbilityPotPercentAtOb10 { get; set; }
            public double? MultiplyDamageBonusPercent { get; set; }
        }

        private sealed class FallbackCostumeEntry : FallbackEntry
        {
        }

        public bool TryGetWeapon(string weaponName, out WeaponInfo info)
        {
            if (_byWeaponName.TryGetValue(weaponName, out info!))
            {
                return true;
            }

            var normalized = NormalizeKey(weaponName);
            return _byWeaponNameNormalized.TryGetValue(normalized, out info!);
        }

        private static string NormalizeKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            return s
                .Trim()
                .Replace("’", "'")
                .Replace("‘", "'")
                .Replace("“", "\"")
                .Replace("”", "\"")
                .Replace("\u00A0", " ")
                .Replace(" ", "")
                .ToLowerInvariant();
        }

        public bool TryGetCostume(string costumeName, out CostumeInfo info) => _byCostumeName.TryGetValue(costumeName, out info!);

        public IReadOnlyList<CostumeInfo> GetCostumesForCharacter(string characterName)
        {
            return _costumesByCharacter.TryGetValue(characterName, out var list) ? list : Array.Empty<CostumeInfo>();
        }
    }

    public sealed class WeaponInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string GachaType { get; set; } = string.Empty;
        public bool IsUltimate { get; set; }
        public string AbilityElement { get; set; } = string.Empty;
        public string AbilityType { get; set; } = string.Empty;
        public string AbilityRange { get; set; } = string.Empty;
        public double? AbilityPotPercentAtOb10 { get; set; }
        public double MultiplyDamageBonusPercent { get; set; }
        public string EffectTextBlob { get; set; } = string.Empty;
    }

    public sealed class CostumeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string AbilityElement { get; set; } = string.Empty;
        public string AbilityType { get; set; } = string.Empty;
        public string AbilityRange { get; set; } = string.Empty;
        public string EffectTextBlob { get; set; } = string.Empty;
    }
}
