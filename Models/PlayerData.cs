using System.ComponentModel.DataAnnotations;

namespace FFVIIEverCrisisAnalyzer.Models
{
    public class PlayerData
    {
        [Required]
        public string PlayerName { get; set; } = string.Empty;

        public string? DiscordName { get; set; }

        [Required]
        public string CharacterName { get; set; } = string.Empty;

        public int Level { get; set; }

        public int HP { get; set; }

        public int Attack { get; set; }

        public int Defense { get; set; }

        public int Magic { get; set; }

        public int MagicDefense { get; set; }

        public int Speed { get; set; }

        public int CriticalRate { get; set; }

        public int Evasion { get; set; }

        public int WeaponLevel { get; set; }

        public int ArmorLevel { get; set; }

        public int AccessoryLevel { get; set; }

        public int AbilityLevel { get; set; }

        public int LimitBreakLevel { get; set; }

        public int OverlordLevel { get; set; }

        public double PowerLevel { get; set; }
    }
}
