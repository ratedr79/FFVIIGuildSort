using System.Text.Json;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class NameCorrectionService
    {
        private readonly Dictionary<string, string> _weaponCorrections = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _outfitCorrections = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _appliedCorrections = new();

        public NameCorrectionService()
        {
            LoadCorrections();
        }

        private void LoadCorrections()
        {
            var filePath = Path.Combine("data", "nameCorrections.json");
            
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var corrections = JsonSerializer.Deserialize<NameCorrections>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (corrections?.Weapons != null)
                {
                    foreach (var kvp in corrections.Weapons)
                    {
                        _weaponCorrections[kvp.Key] = kvp.Value;
                    }
                }

                if (corrections?.Outfits != null)
                {
                    foreach (var kvp in corrections.Outfits)
                    {
                        _outfitCorrections[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to load name corrections: {ex.Message}");
            }
        }

        public string CorrectWeaponName(string weaponName)
        {
            if (_weaponCorrections.TryGetValue(weaponName, out var corrected))
            {
                var logEntry = $"Weapon: '{weaponName}' -> '{corrected}'";
                if (!_appliedCorrections.Contains(logEntry))
                {
                    _appliedCorrections.Add(logEntry);
                    Console.WriteLine($"[Name Correction] {logEntry}");
                }
                return corrected;
            }
            return weaponName;
        }

        public string CorrectOutfitName(string outfitName)
        {
            if (_outfitCorrections.TryGetValue(outfitName, out var corrected))
            {
                var logEntry = $"Outfit: '{outfitName}' -> '{corrected}'";
                if (!_appliedCorrections.Contains(logEntry))
                {
                    _appliedCorrections.Add(logEntry);
                    Console.WriteLine($"[Name Correction] {logEntry}");
                }
                return corrected;
            }
            return outfitName;
        }

        public IReadOnlyList<string> GetAppliedCorrections() => _appliedCorrections.AsReadOnly();

        private class NameCorrections
        {
            public Dictionary<string, string>? Weapons { get; set; }
            public Dictionary<string, string>? Outfits { get; set; }
        }
    }
}
