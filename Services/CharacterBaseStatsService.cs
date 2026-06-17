using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class CharacterBaseStats
    {
        public int Hp { get; init; }
        public int PhysicalAttack { get; init; }
        public int MagicalAttack { get; init; }
        public int PhysicalDefense { get; init; }
        public int MagicalDefense { get; init; }
        public int HealingPower { get; init; }

        public static readonly CharacterBaseStats Zero = new();
    }

    // Character stat sources from FF7EC master data:
    //   - Base: per-(character, level) from CharacterLevel.json (the small raw level value).
    //   - Character Stream: sum of ALL Growth Board (type 1) node stats for the character = the max (all unlocked).
    //   - Role Stream: sum of ALL Growth Board (type 5) node stats for the character = the max.
    // The in-game total a player sees is floor((Base + CharacterStream + RoleStream) x (1 + Highwind%)); the
    // stream sums here reproduce the in-game Character/Role Stream values exactly (verified across characters).
    public sealed class CharacterBaseStatsService
    {
        private const int CharacterStreamBoardType = 1;
        private const int RoleStreamBoardType = 5;

        private readonly Dictionary<(int CharacterId, int Level), CharacterBaseStats> _byCharacterLevel = new();
        private readonly Dictionary<int, CharacterBaseStats> _characterStreamMax = new();
        private readonly Dictionary<int, CharacterBaseStats> _roleStreamMax = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public CharacterBaseStatsService(IWebHostEnvironment environment)
        {
            var dir = Path.Combine(environment.ContentRootPath, "external", "UnknownX7", "FF7EC-Data", "MasterData", "gl");
            LoadLevels(Path.Combine(dir, "CharacterLevel.json"));
            LoadStreamMaxes(Path.Combine(dir, "GrowthBoardGroup.json"), Path.Combine(dir, "GrowthBoardNode.json"));
        }

        public CharacterBaseStats? Get(int characterId, int level)
            => _byCharacterLevel.TryGetValue((characterId, level), out var stats) ? stats : null;

        public CharacterBaseStats GetCharacterStreamMax(int characterId)
            => _characterStreamMax.TryGetValue(characterId, out var stats) ? stats : CharacterBaseStats.Zero;

        public CharacterBaseStats GetRoleStreamMax(int characterId)
            => _roleStreamMax.TryGetValue(characterId, out var stats) ? stats : CharacterBaseStats.Zero;

        public int MaxLevel(int characterId)
        {
            var levels = _byCharacterLevel.Keys.Where(k => k.CharacterId == characterId).Select(k => k.Level).ToList();
            return levels.Count > 0 ? levels.Max() : 0;
        }

        private static List<T> Load<T>(string path)
        {
            if (!File.Exists(path))
            {
                return new List<T>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions) ?? new List<T>();
            }
            catch
            {
                return new List<T>();
            }
        }

        private void LoadLevels(string path)
        {
            foreach (var r in Load<CharacterLevelRaw>(path))
            {
                _byCharacterLevel[(r.CharacterId, r.Level)] = new CharacterBaseStats
                {
                    Hp = r.Hp,
                    PhysicalAttack = r.PhysicalAttack,
                    MagicalAttack = r.MagicalAttack,
                    PhysicalDefense = r.PhysicalDefense,
                    MagicalDefense = r.MagicalDefense,
                    HealingPower = r.HealingPower
                };
            }
        }

        private void LoadStreamMaxes(string groupPath, string nodePath)
        {
            var groupInfoById = Load<GrowthBoardGroupRaw>(groupPath)
                .GroupBy(g => g.Id)
                .ToDictionary(g => g.Key, g => (g.First().TargetId, g.First().GrowthBoardType));

            var characterAcc = new Dictionary<int, int[]>();
            var roleAcc = new Dictionary<int, int[]>();

            foreach (var n in Load<GrowthBoardNodeRaw>(nodePath))
            {
                if (!groupInfoById.TryGetValue(n.GrowthBoardGroupId, out var info))
                {
                    continue;
                }

                var bucket = info.GrowthBoardType switch
                {
                    CharacterStreamBoardType => characterAcc,
                    RoleStreamBoardType => roleAcc,
                    _ => null
                };
                if (bucket == null)
                {
                    continue;
                }

                if (!bucket.TryGetValue(info.TargetId, out var acc))
                {
                    acc = new int[6];
                    bucket[info.TargetId] = acc;
                }

                acc[0] += n.HpNodeStatusValue;
                acc[1] += n.PhysicalAttackNodeStatusValue;
                acc[2] += n.MagicalAttackNodeStatusValue;
                acc[3] += n.PhysicalDefenseNodeStatusValue;
                acc[4] += n.MagicalDefenseNodeStatusValue;
                acc[5] += n.HealingPowerNodeStatusValue;
            }

            foreach (var kvp in characterAcc)
            {
                _characterStreamMax[kvp.Key] = ToStats(kvp.Value);
            }

            foreach (var kvp in roleAcc)
            {
                _roleStreamMax[kvp.Key] = ToStats(kvp.Value);
            }
        }

        private static CharacterBaseStats ToStats(int[] a) => new()
        {
            Hp = a[0],
            PhysicalAttack = a[1],
            MagicalAttack = a[2],
            PhysicalDefense = a[3],
            MagicalDefense = a[4],
            HealingPower = a[5]
        };

        private sealed class CharacterLevelRaw
        {
            public int CharacterId { get; set; }
            public int Level { get; set; }
            public int Hp { get; set; }
            public int PhysicalAttack { get; set; }
            public int MagicalAttack { get; set; }
            public int PhysicalDefense { get; set; }
            public int MagicalDefense { get; set; }
            public int HealingPower { get; set; }
        }

        private sealed class GrowthBoardGroupRaw
        {
            public int Id { get; set; }
            public int TargetId { get; set; }
            public int GrowthBoardType { get; set; }
        }

        private sealed class GrowthBoardNodeRaw
        {
            public int GrowthBoardGroupId { get; set; }
            public int HpNodeStatusValue { get; set; }
            public int PhysicalAttackNodeStatusValue { get; set; }
            public int MagicalAttackNodeStatusValue { get; set; }
            public int PhysicalDefenseNodeStatusValue { get; set; }
            public int MagicalDefenseNodeStatusValue { get; set; }
            public int HealingPowerNodeStatusValue { get; set; }
        }
    }
}
