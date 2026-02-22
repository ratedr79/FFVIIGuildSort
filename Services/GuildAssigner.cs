using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class GuildAssigner
    {
        private readonly IWebHostEnvironment _env;

        public GuildAssigner(IWebHostEnvironment env)
        {
            _env = env;
        }

        public GuildRulesConfig LoadRulesOrDefault()
        {
            var path = Path.Combine(_env.ContentRootPath, "data", "guildRules.json");
            if (!File.Exists(path))
            {
                return new GuildRulesConfig();
            }

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<GuildRulesConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return cfg ?? new GuildRulesConfig();
        }

        public GuildAssignmentResult AssignGuilds(IReadOnlyList<BestTeamResult> rankedTeams, GuildRulesConfig cfg)
        {
            cfg ??= new GuildRulesConfig();

            var result = new GuildAssignmentResult();

            var guildCount = Math.Max(1, cfg.GuildCount);
            var guildSize = Math.Max(1, cfg.GuildSize);

            var lockedByPlayer = cfg.LockedPlayers
                .Where(r => !string.IsNullOrWhiteSpace(r.Player))
                .GroupBy(r => r.Player.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Guild, StringComparer.OrdinalIgnoreCase);

            var excludedByPlayer = cfg.PlayerGuildExclusions
                .Where(r => !string.IsNullOrWhiteSpace(r.Player))
                .GroupBy(r => r.Player.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.SelectMany(x => x.ExcludedGuilds ?? new List<int>()).Distinct().ToHashSet(),
                    StringComparer.OrdinalIgnoreCase
                );

            var ensurePlayers = (cfg.EnsurePlayersPresent ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var players = rankedTeams
                .Select(t => t.InGameName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var p in ensurePlayers)
            {
                if (!players.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    players.Add(p);
                    result.Warnings.Add($"Configured player '{p}' is missing from CSV; added as placeholder (score will be 0)." );
                }
            }

            // Preserve the ranking order for players present in rankedTeams.
            var rankIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rankedTeams.Count; i++)
            {
                var name = rankedTeams[i].InGameName?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !rankIndex.ContainsKey(name))
                {
                    rankIndex[name] = i;
                }
            }

            players = players
                .OrderBy(p => rankIndex.TryGetValue(p, out var idx) ? idx : int.MaxValue)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var guildMembers = Enumerable.Range(1, guildCount)
                .ToDictionary(g => g, _ => new List<string>());

            var assigned = new Dictionary<string, PlayerGuildAssignment>(StringComparer.OrdinalIgnoreCase);

            // 1) Place locked players first.
            foreach (var kvp in lockedByPlayer)
            {
                var player = kvp.Key;
                var guild = kvp.Value;
                if (guild < 1 || guild > guildCount)
                {
                    result.Warnings.Add($"Locked player '{player}' has invalid guild '{guild}'." );
                    continue;
                }

                if (excludedByPlayer.TryGetValue(player, out var ex) && ex.Contains(guild))
                {
                    result.Warnings.Add($"Locked player '{player}' is also excluded from guild {guild}; keeping locked placement." );
                }

                if (!guildMembers[guild].Contains(player, StringComparer.OrdinalIgnoreCase))
                {
                    guildMembers[guild].Add(player);
                }

                assigned[player] = new PlayerGuildAssignment { Player = player, Guild = guild, Reason = "Locked" };
            }

            // 2) Fill remaining slots in ranking order.
            foreach (var player in players)
            {
                if (assigned.ContainsKey(player))
                {
                    continue;
                }

                var excluded = excludedByPlayer.TryGetValue(player, out var ex) ? ex : new HashSet<int>();

                int? chosenGuild = null;
                for (int guild = 1; guild <= guildCount; guild++)
                {
                    if (excluded.Contains(guild))
                    {
                        continue;
                    }

                    if (guildMembers[guild].Count >= guildSize)
                    {
                        continue;
                    }

                    chosenGuild = guild;
                    break;
                }

                if (chosenGuild == null)
                {
                    // If all guilds are full, put them in the last guild anyway and warn.
                    chosenGuild = guildCount;
                    result.Warnings.Add($"All guilds are full; assigned '{player}' to Guild {guildCount}." );
                }

                guildMembers[chosenGuild.Value].Add(player);
                assigned[player] = new PlayerGuildAssignment
                {
                    Player = player,
                    Guild = chosenGuild.Value,
                    Reason = excluded.Count > 0 ? "Excluded from some guilds" : "By rank"
                };
            }

            result.Assignments = assigned.Values
                .OrderBy(a => a.Guild)
                .ThenBy(a => rankIndex.TryGetValue(a.Player, out var idx) ? idx : int.MaxValue)
                .ThenBy(a => a.Player, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return result;
        }
    }
}
