using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public class PowerLevelCalculator
    {
        public double CalculatePowerLevel(PlayerData player)
        {
            // Base stats weighted calculation
            double basePower = (player.HP * 0.1) + 
                              (player.Attack * 2.0) + 
                              (player.Defense * 1.5) + 
                              (player.Magic * 2.2) + 
                              (player.MagicDefense * 1.5) + 
                              (player.Speed * 1.8) +
                              (player.CriticalRate * 0.05) +
                              (player.Evasion * 0.03);

            // Equipment multipliers
            double equipmentMultiplier = 1.0 + 
                                        (player.WeaponLevel * 0.1) + 
                                        (player.ArmorLevel * 0.08) + 
                                        (player.AccessoryLevel * 0.05);

            // Ability and special bonuses
            double abilityBonus = (player.AbilityLevel * 50) + 
                                 (player.LimitBreakLevel * 100) + 
                                 (player.OverlordLevel * 200);

            // Level scaling factor
            double levelScaling = 1.0 + (player.Level * 0.05);

            // Final power level calculation
            double finalPower = (basePower * equipmentMultiplier * levelScaling) + abilityBonus;

            return Math.Round(finalPower, 2);
        }

        public List<PlayerData> RankPlayersByPowerLevel(List<PlayerData> players)
        {
            return players.OrderByDescending(p => p.PowerLevel).ToList();
        }

        public PowerAnalysisResult AnalyzePowerLevels(List<PlayerData> players)
        {
            var rankedPlayers = RankPlayersByPowerLevel(players);
            
            return new PowerAnalysisResult
            {
                TotalPlayers = players.Count,
                AveragePowerLevel = players.Average(p => p.PowerLevel),
                MaxPowerLevel = players.Max(p => p.PowerLevel),
                MinPowerLevel = players.Min(p => p.PowerLevel),
                RankedPlayers = rankedPlayers,
                TopPlayers = rankedPlayers.Take(10).ToList(),
                PowerDistribution = GetPowerDistribution(players)
            };
        }

        private Dictionary<string, int> GetPowerDistribution(List<PlayerData> players)
        {
            var distribution = new Dictionary<string, int>
            {
                ["S-Tier (90k+)"] = 0,
                ["A-Tier (70k-90k)"] = 0,
                ["B-Tier (50k-70k)"] = 0,
                ["C-Tier (30k-50k)"] = 0,
                ["D-Tier (<30k)"] = 0
            };

            foreach (var player in players)
            {
                if (player.PowerLevel >= 90000) distribution["S-Tier (90k+)"]++;
                else if (player.PowerLevel >= 70000) distribution["A-Tier (70k-90k)"]++;
                else if (player.PowerLevel >= 50000) distribution["B-Tier (50k-70k)"]++;
                else if (player.PowerLevel >= 30000) distribution["C-Tier (30k-50k)"]++;
                else distribution["D-Tier (<30k)"]++;
            }

            return distribution;
        }
    }

    public class PowerAnalysisResult
    {
        public int TotalPlayers { get; set; }
        public double AveragePowerLevel { get; set; }
        public double MaxPowerLevel { get; set; }
        public double MinPowerLevel { get; set; }
        public List<PlayerData> RankedPlayers { get; set; } = new();
        public List<PlayerData> TopPlayers { get; set; } = new();
        public Dictionary<string, int> PowerDistribution { get; set; } = new();
    }
}
