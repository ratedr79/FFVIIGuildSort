using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class WeaponCatalog
    {
        private sealed record AdditionalWeaponRow(string? Character, string? Weapon, string? Ability1, string? Ability2, string? Ability3);
        private sealed record AdditionalOutfitRow(
            string? Character,
            string? Outfit,
            string? Command,
            string? Ability1,
            string? Ability2,
            string? Ability3,
            string? Ability4,
            string? Ability5,
            string? Ability6,
            string? Ability7,
            string? Ability8,
            string? Ability9,
            string? Ability10);

        private readonly Dictionary<string, WeaponInfo> _byWeaponName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WeaponInfo> _byWeaponNameNormalized = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CostumeInfo> _byCostumeName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<CostumeInfo>> _costumesByCharacter = new(StringComparer.OrdinalIgnoreCase);
        private readonly NameCorrectionService _nameCorrectionService;

        public IReadOnlyDictionary<string, WeaponInfo> ByWeaponName => _byWeaponName;
        public IReadOnlyDictionary<string, CostumeInfo> ByCostumeName => _byCostumeName;

        public WeaponCatalog(IWebHostEnvironment env, NameCorrectionService nameCorrectionService)
        {
            _nameCorrectionService = nameCorrectionService;
            
            var additionalWeapons = LoadAdditionalWeaponData(env);
            var additionalOutfits = LoadAdditionalOutfitData(env);
            var additionalUltimateWeapons = LoadAdditionalUltimateWeaponData(env);

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
                    var equipmentType = (csv.GetField("GachaType") ?? string.Empty).Trim();
                    var abilityElement = (csv.GetField("Ability Element") ?? string.Empty).Trim();
                    var abilityType = (csv.GetField("Ability Type") ?? string.Empty).Trim();
                    var abilityRange = (csv.GetField("Ability Range") ?? string.Empty).Trim();
                    var abilityText = (csv.GetField("Ability Text") ?? string.Empty).Trim();
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

                    if (!string.IsNullOrWhiteSpace(abilityText))
                    {
                        effectText = string.Join(" | ", new[] { effectText, abilityText }
                            .Where(s => !string.IsNullOrWhiteSpace(s)));
                    }

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
                        var custom = (csv.GetField($"Effect{i}_Custom") ?? string.Empty).Trim();
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

                    AddWeaponOrCostume(name, character, equipmentType, abilityElement, abilityType, abilityRange, abilityPotPercent, multiplyDamageBonusPercent, effectText);
                    if (equipmentType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase))
                    {
                        TryEnrichWeaponFromAdditional(additionalUltimateWeapons, name, _byWeaponName[name]);
                    }
                    else
                    {
                        TryEnrichWeaponFromAdditional(additionalWeapons, name, _byWeaponName[name]);
                    }
                    if (equipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
                    {
                        TryEnrichCostumeFromAdditional(additionalOutfits, name, _byCostumeName);
                    }
                }
            }

            LoadAdditionalJsonFallbacks(additionalWeapons, additionalOutfits, additionalUltimateWeapons);
        }

        private void AddWeaponOrCostume(
            string name,
            string character,
            string equipmentType,
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
                EquipmentType = equipmentType,
                IsUltimate = equipmentType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase),
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

            if (equipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
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

        private static Dictionary<string, AdditionalWeaponRow> LoadAdditionalWeaponData(IWebHostEnvironment env)
        {
            var path = Path.Combine(env.ContentRootPath, "data", "additionalWeaponData.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, AdditionalWeaponRow>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(path);
                var rows = JsonSerializer.Deserialize<List<AdditionalWeaponRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return rows == null
                    ? new Dictionary<string, AdditionalWeaponRow>(StringComparer.OrdinalIgnoreCase)
                    : rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.Weapon))
                        .GroupBy(r => r.Weapon!.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, AdditionalWeaponRow>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, AdditionalOutfitRow> LoadAdditionalOutfitData(IWebHostEnvironment env)
        {
            var path = Path.Combine(env.ContentRootPath, "data", "additionalOutfitData.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, AdditionalOutfitRow>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(path);
                var rows = JsonSerializer.Deserialize<List<AdditionalOutfitRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return rows == null
                    ? new Dictionary<string, AdditionalOutfitRow>(StringComparer.OrdinalIgnoreCase)
                    : rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.Outfit))
                        .GroupBy(r => r.Outfit!.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, AdditionalOutfitRow>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, AdditionalWeaponRow> LoadAdditionalUltimateWeaponData(IWebHostEnvironment env)
        {
            var path = Path.Combine(env.ContentRootPath, "data", "additionalUltimateWeaponData.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, AdditionalWeaponRow>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(path);
                var rows = JsonSerializer.Deserialize<List<AdditionalWeaponRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return rows == null
                    ? new Dictionary<string, AdditionalWeaponRow>(StringComparer.OrdinalIgnoreCase)
                    : rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.Weapon))
                        .GroupBy(r => r.Weapon!.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, AdditionalWeaponRow>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string BuildAdditionalWeaponEffectText(AdditionalWeaponRow row)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Ability1)) parts.Add(row.Ability1!.Trim());
            if (!string.IsNullOrWhiteSpace(row.Ability2)) parts.Add(row.Ability2!.Trim());
            if (!string.IsNullOrWhiteSpace(row.Ability3)) parts.Add(row.Ability3!.Trim());
            return string.Join(" | ", parts);
        }

        private static string BuildAdditionalOutfitEffectText(AdditionalOutfitRow row)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Command)) parts.Add(row.Command!.Trim());
            var abilities = new[] { row.Ability1, row.Ability2, row.Ability3, row.Ability4, row.Ability5, row.Ability6, row.Ability7, row.Ability8, row.Ability9, row.Ability10 };
            foreach (var a in abilities)
            {
                if (!string.IsNullOrWhiteSpace(a)) parts.Add(a!.Trim());
            }
            return string.Join(" | ", parts);
        }

        private static void AppendEffectText(ref string target, string? extra)
        {
            if (string.IsNullOrWhiteSpace(extra))
            {
                return;
            }

            target = string.Join(" | ", new[] { target, extra }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static void TryEnrichWeaponFromAdditional(Dictionary<string, AdditionalWeaponRow> additionalWeapons, string weaponName, WeaponInfo weapon)
        {
            if (!additionalWeapons.TryGetValue(weaponName, out var row))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(weapon.Character) && !string.IsNullOrWhiteSpace(row.Character))
            {
                weapon.Character = row.Character!.Trim();
            }

            if (string.IsNullOrWhiteSpace(weapon.AdditionalAbility1) && !string.IsNullOrWhiteSpace(row.Ability1))
            {
                weapon.AdditionalAbility1 = row.Ability1!.Trim();
            }

            if (string.IsNullOrWhiteSpace(weapon.AdditionalAbility2) && !string.IsNullOrWhiteSpace(row.Ability2))
            {
                weapon.AdditionalAbility2 = row.Ability2!.Trim();
            }

            if (string.IsNullOrWhiteSpace(weapon.AdditionalAbility3) && !string.IsNullOrWhiteSpace(row.Ability3))
            {
                weapon.AdditionalAbility3 = row.Ability3!.Trim();
            }

            var extra = BuildAdditionalWeaponEffectText(row);
            var blob = weapon.EffectTextBlob;
            AppendEffectText(ref blob, extra);
            weapon.EffectTextBlob = blob;
        }

        private static void TryEnrichCostumeFromAdditional(Dictionary<string, AdditionalOutfitRow> additionalOutfits, string name, Dictionary<string, CostumeInfo> costumesByName)
        {
            if (!costumesByName.TryGetValue(name, out var costume))
            {
                return;
            }

            if (!additionalOutfits.TryGetValue(name, out var row))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(costume.Character) && !string.IsNullOrWhiteSpace(row.Character))
            {
                costume.Character = row.Character!.Trim();
            }

            var newAbilities = new[]
                {
                    row.Command,
                    row.Ability1,
                    row.Ability2,
                    row.Ability3,
                    row.Ability4,
                    row.Ability5,
                    row.Ability6,
                    row.Ability7,
                    row.Ability8,
                    row.Ability9,
                    row.Ability10
                }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToList();

            if (newAbilities.Count > 0)
            {
                costume.AdditionalAbilities = newAbilities;
            }

            var extra = BuildAdditionalOutfitEffectText(row);
            var blob = costume.EffectTextBlob;
            AppendEffectText(ref blob, extra);
            costume.EffectTextBlob = blob;
        }

        private void LoadAdditionalJsonFallbacks(Dictionary<string, AdditionalWeaponRow> additionalWeapons, Dictionary<string, AdditionalOutfitRow> additionalOutfits, Dictionary<string, AdditionalWeaponRow> additionalUltimateWeapons)
        {
            // Weapons: add any missing weapons from additionalWeaponData.
            foreach (var kvp in additionalWeapons)
            {
                var weaponName = kvp.Key;
                var row = kvp.Value;
                if (_byWeaponName.ContainsKey(weaponName))
                {
                    continue;
                }

                var character = (row.Character ?? string.Empty).Trim();
                var effectText = BuildAdditionalWeaponEffectText(row);
                AddWeaponOrCostume(
                    weaponName.Trim(),
                    character,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    0,
                    effectText);

                if (_byWeaponName.TryGetValue(weaponName, out var w))
                {
                    w.AdditionalAbility1 = string.IsNullOrWhiteSpace(row.Ability1) ? null : row.Ability1!.Trim();
                    w.AdditionalAbility2 = string.IsNullOrWhiteSpace(row.Ability2) ? null : row.Ability2!.Trim();
                    w.AdditionalAbility3 = string.IsNullOrWhiteSpace(row.Ability3) ? null : row.Ability3!.Trim();
                }
            }

            // Ultimate Weapons: add any missing ultimate weapons from additionalUltimateWeaponData.
            foreach (var kvp in additionalUltimateWeapons)
            {
                var weaponName = kvp.Key;
                var row = kvp.Value;
                if (_byWeaponName.ContainsKey(weaponName))
                {
                    continue;
                }

                var character = (row.Character ?? string.Empty).Trim();
                var effectText = BuildAdditionalWeaponEffectText(row);
                AddWeaponOrCostume(
                    weaponName.Trim(),
                    character,
                    "Ultimate",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    0,
                    effectText);

                if (_byWeaponName.TryGetValue(weaponName, out var w))
                {
                    w.AdditionalAbility1 = string.IsNullOrWhiteSpace(row.Ability1) ? null : row.Ability1!.Trim();
                    w.AdditionalAbility2 = string.IsNullOrWhiteSpace(row.Ability2) ? null : row.Ability2!.Trim();
                    w.AdditionalAbility3 = string.IsNullOrWhiteSpace(row.Ability3) ? null : row.Ability3!.Trim();
                }
            }

            // Outfits: add any missing costumes from additionalOutfitData.
            foreach (var kvp in additionalOutfits)
            {
                var outfitName = kvp.Key;
                var row = kvp.Value;
                if (_byCostumeName.ContainsKey(outfitName))
                {
                    continue;
                }

                var character = (row.Character ?? string.Empty).Trim();
                var effectText = BuildAdditionalOutfitEffectText(row);
                AddWeaponOrCostume(
                    outfitName.Trim(),
                    character,
                    "Costume",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    0,
                    effectText);

                if (_byCostumeName.TryGetValue(outfitName, out var c))
                {
                    c.AdditionalAbilities = new[]
                        {
                            row.Command,
                            row.Ability1,
                            row.Ability2,
                            row.Ability3,
                            row.Ability4,
                            row.Ability5,
                            row.Ability6,
                            row.Ability7,
                            row.Ability8,
                            row.Ability9,
                            row.Ability10
                        }
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!.Trim())
                        .ToList();
                }
            }
        }

        public bool TryGetWeapon(string weaponName, out WeaponInfo info)
        {
            // Apply name corrections first
            var correctedName = _nameCorrectionService.CorrectWeaponName(weaponName);
            
            if (_byWeaponName.TryGetValue(correctedName, out info!))
            {
                return true;
            }

            var normalized = NormalizeKey(correctedName);
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

        public bool TryGetCostume(string costumeName, out CostumeInfo info)
        {
            // Apply name corrections first
            var correctedName = _nameCorrectionService.CorrectOutfitName(costumeName);
            return _byCostumeName.TryGetValue(correctedName, out info!);
        }

        public IReadOnlyList<CostumeInfo> GetCostumesForCharacter(string characterName)
        {
            return _costumesByCharacter.TryGetValue(characterName, out var list) ? list : Array.Empty<CostumeInfo>();
        }
    }

    public sealed class WeaponInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string EquipmentType { get; set; } = string.Empty;
        public string GachaType
        {
            get => EquipmentType;
            set => EquipmentType = value;
        }
        public bool IsUltimate { get; set; }
        public string AbilityElement { get; set; } = string.Empty;
        public string AbilityType { get; set; } = string.Empty;
        public string AbilityRange { get; set; } = string.Empty;
        public double? AbilityPotPercentAtOb10 { get; set; }
        public double MultiplyDamageBonusPercent { get; set; }
        public string EffectTextBlob { get; set; } = string.Empty;
        public string? AdditionalAbility1 { get; set; }
        public string? AdditionalAbility2 { get; set; }
        public string? AdditionalAbility3 { get; set; }
    }

    public sealed class CostumeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string AbilityElement { get; set; } = string.Empty;
        public string AbilityType { get; set; } = string.Empty;
        public string AbilityRange { get; set; } = string.Empty;
        public string EffectTextBlob { get; set; } = string.Empty;
        public List<string> AdditionalAbilities { get; set; } = new();
    }
}
