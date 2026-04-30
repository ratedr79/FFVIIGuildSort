using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class WeaponCatalog
    {
        private readonly Dictionary<string, WeaponInfo> _byWeaponName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WeaponInfo> _byWeaponNameNormalized = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CostumeInfo> _byCostumeName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<CostumeInfo>> _costumesByCharacter = new(StringComparer.OrdinalIgnoreCase);
        private readonly NameCorrectionService _nameCorrectionService;
        private readonly WeaponSearchDataService _weaponSearchDataService;

        public IReadOnlyDictionary<string, WeaponInfo> ByWeaponName => _byWeaponName;
        public IReadOnlyDictionary<string, CostumeInfo> ByCostumeName => _byCostumeName;

        public WeaponCatalog(
            IWebHostEnvironment env,
            NameCorrectionService nameCorrectionService,
            WeaponSearchDataService weaponSearchDataService,
            IOptions<WeaponCatalogOptions>? options = null)
        {
            _nameCorrectionService = nameCorrectionService;
            _weaponSearchDataService = weaponSearchDataService;

            var useWeaponServiceForPowerAnalyzer = options?.Value?.UseWeaponServiceForPowerAnalyzer ?? false;
            if (useWeaponServiceForPowerAnalyzer)
            {
                LoadFromWeaponService();
            }
            else
            {
                LoadFromTsv(env);
            }

            EnrichFromGearSearch();
        }

        private void LoadFromWeaponService()
        {
            foreach (var item in _weaponSearchDataService.GetWeapons())
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                var effectTextParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.AbilityText))
                {
                    effectTextParts.Add(item.AbilityText.Trim());
                }

                if (item.EffectTags.Count > 0)
                {
                    effectTextParts.AddRange(item.EffectTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
                }

                var synergyEffectNames = item.EffectTags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var effectText = string.Join(" | ", effectTextParts
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                AddWeaponOrCostume(
                    item.Name.Trim(),
                    item.Character.Trim(),
                    item.EquipmentType.Trim(),
                    item.Element.Trim(),
                    item.AbilityType.Trim(),
                    item.Range.Trim(),
                    item.DamagePercent,
                    0,
                    effectText,
                    synergyEffectNames);
            }
        }

        private void LoadFromTsv(IWebHostEnvironment env)
        {
            var preferredPath = Path.Combine(env.ContentRootPath, "external", "CypherSignal", "ff7ec", "weaponData.tsv");
            var fallbackPath = Path.Combine(env.ContentRootPath, "data", "weaponData.tsv");
            var dataPath = File.Exists(preferredPath) ? preferredPath : fallbackPath;
            if (!File.Exists(dataPath))
            {
                return;
            }

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

                var synergyEffectNames = new List<string>();
                var metaParts = new List<string>();
                for (int i = 0; i <= 3; i++)
                {
                    var eff = (csv.GetField($"Effect{i}") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(eff))
                    {
                        continue;
                    }

                    if (!eff.Equals("Multiply Damage", StringComparison.OrdinalIgnoreCase))
                    {
                        synergyEffectNames.Add(eff);
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

                AddWeaponOrCostume(name, character, equipmentType, abilityElement, abilityType, abilityRange, abilityPotPercent, multiplyDamageBonusPercent, effectText, synergyEffectNames);
            }
        }

        public void RefreshFromGearSearch()
        {
            ClearGearSearchEnrichment();
            EnrichFromGearSearch();
        }

        private void ClearGearSearchEnrichment()
        {
            foreach (var weapon in _byWeaponName.Values)
            {
                weapon.PotPercentByOb.Clear();
                weapon.GearSearchRAbilities.Clear();
                weapon.GearSearchRAbilityDescriptions.Clear();
                weapon.HasCustomizations = false;
                weapon.CustomizationDescriptions.Clear();
                weapon.GearSearchEnriched = false;
                weapon.AdditionalAbility1 = string.Empty;
                weapon.AdditionalAbility2 = string.Empty;
                weapon.AdditionalAbility3 = string.Empty;
            }

            foreach (var costume in _byCostumeName.Values)
            {
                costume.GearSearchRAbilities.Clear();
                costume.GearSearchEnriched = false;
                costume.AdditionalAbilities = new List<string>();
            }
        }

        private void EnrichFromGearSearch()
        {
            // Enrich weapons with real pot% per OB and R abilities
            foreach (var kvp in _byWeaponName)
            {
                var weapon = kvp.Value;

                // Get R abilities and customizations at OB10 / max level
                var ob10Enrichment = _weaponSearchDataService.GetWeaponEnrichmentAtOb(weapon.Name, 10);
                if (ob10Enrichment == null)
                    continue;

                weapon.GearSearchEnriched = true;

                // Pre-compute pot% for OB 0-10 (use customized pot% when available)
                for (int ob = 0; ob <= 10; ob++)
                {
                    var enrichment = _weaponSearchDataService.GetWeaponEnrichmentAtOb(weapon.Name, ob);
                    if (enrichment != null)
                    {
                        var customPot = TryGetCustomizedPotPercent(enrichment);
                        weapon.PotPercentByOb[ob] = customPot ?? enrichment.DamagePercent;
                    }
                }

                // R Abilities from GearSearch (names only)
                weapon.GearSearchRAbilities = ob10Enrichment.RAbilities
                    .Select(r => r.SkillName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                // R Abilities with point values for display (e.g. "Boost MATK (All Allies) +46 pts")
                weapon.GearSearchRAbilityDescriptions = ob10Enrichment.RAbilities
                    .Where(r => !string.IsNullOrWhiteSpace(r.SkillName))
                    .Select(r => r.TotalPoints > 0 ? $"{r.SkillName} +{r.TotalPoints} pts" : r.SkillName)
                    .ToList();

                // Populate AdditionalAbility fields from GearSearch R abilities for backward compat
                var rAbilityNames = weapon.GearSearchRAbilities;
                if (rAbilityNames.Count > 0) weapon.AdditionalAbility1 = rAbilityNames[0];
                if (rAbilityNames.Count > 1) weapon.AdditionalAbility2 = rAbilityNames[1];
                if (rAbilityNames.Count > 2) weapon.AdditionalAbility3 = rAbilityNames[2];

                // Customizations
                weapon.HasCustomizations = ob10Enrichment.HasCustomizations;
                weapon.CustomizationDescriptions = ob10Enrichment.Customizations
                    .Select(c => c.Description)
                    .ToList();

                // Also enrich EffectTextBlob with R ability names for synergy detection
                var rAbilityText = string.Join(" | ", rAbilityNames);
                if (!string.IsNullOrWhiteSpace(rAbilityText))
                {
                    var blob = weapon.EffectTextBlob;
                    AppendEffectText(ref blob, rAbilityText);
                    weapon.EffectTextBlob = blob;
                }
            }

            // Enrich costumes with R abilities from GearSearch
            foreach (var kvp in _byCostumeName)
            {
                var costume = kvp.Value;
                var enrichment = _weaponSearchDataService.GetWeaponEnrichment(costume.Name, 10);
                if (enrichment == null)
                    continue;

                costume.GearSearchEnriched = true;
                costume.GearSearchRAbilities = enrichment.RAbilities
                    .Select(r => r.SkillName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                // Populate AdditionalAbilities from GearSearch R abilities
                if (costume.GearSearchRAbilities.Count > 0)
                {
                    costume.AdditionalAbilities = costume.GearSearchRAbilities;
                }

                // Enrich EffectTextBlob
                var rAbilityText = string.Join(" | ", costume.GearSearchRAbilities);
                if (!string.IsNullOrWhiteSpace(rAbilityText))
                {
                    var blob = costume.EffectTextBlob;
                    AppendEffectText(ref blob, rAbilityText);
                    costume.EffectTextBlob = blob;
                }
            }
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
            string effectText,
            List<string>? synergyEffects = null)
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
                EffectTextBlob = effectText,
                SynergyEffects = synergyEffects ?? new()
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

        private static readonly Regex DamagePotencyNewRegex = new(
            @"new\s+([\d,]+\.?\d*)%",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// If the enrichment has a "Damage Potency" customization (Heart slot),
        /// parse the "new Y%" value and return it. Otherwise returns null.
        /// </summary>
        private static double? TryGetCustomizedPotPercent(FFVIIEverCrisisAnalyzer.Models.WeaponEnrichmentResult enrichment)
        {
            foreach (var cust in enrichment.Customizations)
            {
                if (cust.Kind != "Damage Upgrade")
                    continue;

                var match = DamagePotencyNewRegex.Match(cust.Description);
                if (match.Success)
                {
                    var raw = match.Groups[1].Value.Replace(",", "");
                    if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var newPot))
                        return newPot;
                }
            }
            return null;
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

        public bool TryGetWeapon(string weaponName, out WeaponInfo info)
        {
            // Apply name corrections first
            var correctedName = _nameCorrectionService.CorrectWeaponName(weaponName);
            
            if (_byWeaponName.TryGetValue(correctedName, out info!))
            {
                return true;
            }

            var normalized = NormalizeKey(correctedName);
            if (_byWeaponNameNormalized.TryGetValue(normalized, out info!))
            {
                return true;
            }

            // Fallback: try to resolve from GearSearch data
            return TryCreateFromGearSearch(correctedName, out info!);
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
            if (_byCostumeName.TryGetValue(correctedName, out info!))
            {
                return true;
            }

            // Fallback: try to resolve from GearSearch data
            // TryCreateFromGearSearch will add it as a costume if EquipmentType is Costume
            if (TryCreateFromGearSearch(correctedName, out _))
            {
                return _byCostumeName.TryGetValue(correctedName, out info!);
            }

            return false;
        }

        public IReadOnlyList<CostumeInfo> GetCostumesForCharacter(string characterName)
        {
            return _costumesByCharacter.TryGetValue(characterName, out var list) ? list : Array.Empty<CostumeInfo>();
        }

        private bool TryCreateFromGearSearch(string itemName, out WeaponInfo info)
        {
            info = null!;

            var searchItem = _weaponSearchDataService.TryGetWeaponSearchItemByName(itemName);
            if (searchItem == null)
                return false;

            var isCostume = searchItem.EquipmentType.Equals("Costume", StringComparison.OrdinalIgnoreCase);
            var isUltimate = searchItem.EquipmentType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase);

            // If the weapon already exists (e.g. loaded from TSV), don't overwrite it —
            // just return the existing entry to preserve TSV effect data.
            if (_byWeaponName.TryGetValue(searchItem.Name, out var existing))
            {
                info = existing;
                return true;
            }

            // Create the entry via AddWeaponOrCostume (populates both weapon and costume dictionaries)
            AddWeaponOrCostume(
                searchItem.Name,
                searchItem.Character,
                searchItem.EquipmentType,
                searchItem.Element,
                searchItem.AbilityType,
                searchItem.Range,
                searchItem.DamagePercent,
                0,
                string.Empty);

            // Now enrich it from GearSearch
            if (_byWeaponName.TryGetValue(searchItem.Name, out var weapon))
            {
                weapon.GearSearchEnriched = true;

                // Pre-compute pot% for OB 0-10 (use customized pot% when available)
                int maxOb = isUltimate ? 0 : 10;
                for (int ob = 0; ob <= maxOb; ob++)
                {
                    var enrichment = _weaponSearchDataService.GetWeaponEnrichmentAtOb(searchItem.Name, ob);
                    if (enrichment != null)
                    {
                        var customPot = TryGetCustomizedPotPercent(enrichment);
                        weapon.PotPercentByOb[ob] = customPot ?? enrichment.DamagePercent;
                    }
                }

                // R Abilities
                var ob10Enrichment = _weaponSearchDataService.GetWeaponEnrichment(searchItem.Name, isUltimate ? 0 : 10);
                if (ob10Enrichment != null)
                {
                    weapon.GearSearchRAbilities = ob10Enrichment.RAbilities
                        .Select(r => r.SkillName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();

                    weapon.GearSearchRAbilityDescriptions = ob10Enrichment.RAbilities
                        .Where(r => !string.IsNullOrWhiteSpace(r.SkillName))
                        .Select(r => r.TotalPoints > 0 ? $"{r.SkillName} +{r.TotalPoints} pts" : r.SkillName)
                        .ToList();

                    var rAbilityNames = weapon.GearSearchRAbilities;
                    if (rAbilityNames.Count > 0) weapon.AdditionalAbility1 = rAbilityNames[0];
                    if (rAbilityNames.Count > 1) weapon.AdditionalAbility2 = rAbilityNames[1];
                    if (rAbilityNames.Count > 2) weapon.AdditionalAbility3 = rAbilityNames[2];

                    weapon.HasCustomizations = ob10Enrichment.HasCustomizations;
                    weapon.CustomizationDescriptions = ob10Enrichment.Customizations
                        .Select(c => c.Description)
                        .ToList();

                    var rAbilityText = string.Join(" | ", rAbilityNames);
                    if (!string.IsNullOrWhiteSpace(rAbilityText))
                    {
                        var blob = weapon.EffectTextBlob;
                        AppendEffectText(ref blob, rAbilityText);
                        weapon.EffectTextBlob = blob;
                    }
                }

                // Also enrich the costume entry if this is a costume
                if (isCostume && _byCostumeName.TryGetValue(searchItem.Name, out var costume))
                {
                    costume.GearSearchEnriched = true;
                    costume.GearSearchRAbilities = weapon.GearSearchRAbilities;
                    if (costume.GearSearchRAbilities.Count > 0)
                    {
                        costume.AdditionalAbilities = costume.GearSearchRAbilities;
                    }

                    var rAbilityText = string.Join(" | ", costume.GearSearchRAbilities);
                    if (!string.IsNullOrWhiteSpace(rAbilityText))
                    {
                        var blob = costume.EffectTextBlob;
                        AppendEffectText(ref blob, rAbilityText);
                        costume.EffectTextBlob = blob;
                    }
                }

                info = weapon;
                return true;
            }

            return false;
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

        // TSV Effect names (e.g. "Fire Damage Up", "Exploit Weakness")
        public List<string> SynergyEffects { get; set; } = new();

        // GearSearch enrichment data
        public Dictionary<int, double> PotPercentByOb { get; set; } = new();
        public List<string> GearSearchRAbilities { get; set; } = new();
        public List<string> GearSearchRAbilityDescriptions { get; set; } = new();
        public bool HasCustomizations { get; set; }
        public List<string> CustomizationDescriptions { get; set; } = new();
        public bool GearSearchEnriched { get; set; }
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

        // GearSearch enrichment data
        public List<string> GearSearchRAbilities { get; set; } = new();
        public bool GearSearchEnriched { get; set; }
    }
}
