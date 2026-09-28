using System.Text.Json;
using System.Text.RegularExpressions;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services;

/// <summary>
/// Turns an EOS account export (raw AccountInfo JSON) into readable player stats.
/// IDs are resolved to names through the UnknownX7 master data + English localization.
/// </summary>
public sealed class EosPlayerStatsService
{
    // Gacha consumption types that spend crystals (the rest are tickets / free draws).
    private static readonly HashSet<int> CrystalConsumptionTypes = new() { 1, 2, 5 };

    private static readonly Dictionary<int, string> GachaTypeLabels = new()
    {
        [0] = "Featured",
        [1] = "Tutorial",
        [2] = "Step Up",
        [3] = "Ticket",
        [4] = "Limit Break",
    };

    // Inline icons used in localized names, mapped to the characters players type for them.
    private static readonly Dictionary<string, string> SpriteText = new()
    {
        ["5"] = "Heal", ["67"] = "✖", ["69"] = "▲", ["71"] = "⬤", ["65"] = "■", ["73"] = "◆",
        ["128"] = "Non-elemental", ["129"] = "Wind", ["130"] = "Water", ["131"] = "Thunder", ["133"] = "Ice",
        ["134"] = "Holy", ["135"] = "Fire", ["136"] = "Earth",
        ["192"] = "★", ["193"] = "★", ["195"] = "★", ["196"] = "★", ["197"] = "★", ["198"] = "★", ["782"] = "Stamina ",
    };

    private static readonly Regex SpriteRegex = new("<sprite=\"tmp_icon\" index=(\\d+)>", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new("\\s+", RegexOptions.Compiled);

    private readonly ILogger<EosPlayerStatsService> _logger;
    private readonly Lazy<MasterData> _master;

    public EosPlayerStatsService(ILogger<EosPlayerStatsService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        var basePath = Path.Combine(environment.ContentRootPath, "external", "UnknownX7", "FF7EC-Data");
        _master = new Lazy<MasterData>(() => MasterData.Load(basePath, _logger));
    }

    public EosPlayerStats Parse(Stream json, bool includePurchases = false)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("AccountInfo", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("This doesn't look like an EOS account export: no \"AccountInfo\" section found.");
        }

        var m = _master.Value;
        var stats = new EosPlayerStats();

        var profile = List(info, "UserProfileList").FirstOrDefault();
        if (profile.ValueKind == JsonValueKind.Object)
        {
            stats.PlayerName = Str(profile, "Name") ?? string.Empty;
            stats.ProfileMessage = Str(profile, "Message");
        }
        stats.GuildName = List(info, "SharedGuildBaseList").Select(g => Str(g, "Name")).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        // Currencies
        foreach (var stone in root.TryGetProperty("OtherInfo", out var other) ? List(other, "UserStoneList") : Enumerable.Empty<JsonElement>())
        {
            var id = Long(stone, "StoneId");
            if (id == 1) stats.RedCrystals = Long(stone, "Count");
            else if (id == 2) stats.BlueCrystals = Long(stone, "Count");
        }

        // Unclaimed gift-box rewards count as owned (matches the community stats bot).
        var pendingGifts = new Dictionary<long, long>();
        foreach (var gift in List(root, "GiftsInfo"))
        {
            if (Long(gift, "RewardType") != 1) continue; // 1 = item
            var id = Long(gift, "TargetId");
            pendingGifts[id] = pendingGifts.GetValueOrDefault(id) + Long(gift, "Count");
        }

        foreach (var item in List(info, "UserItemList"))
        {
            var id = Long(item, "ItemId");
            var pending = pendingGifts.GetValueOrDefault(id);
            var count = Long(item, "Count") + pending;
            var total = Long(item, "TotalObtainCount") + pending;
            if (id == 1)
            {
                stats.Gil = new EosCurrency { Count = count, TotalObtained = total };
                continue;
            }
            stats.Items.Add(new EosItemStat
            {
                Id = id,
                Name = m.ItemNames.TryGetValue(id, out var n) ? n : $"Item #{id}",
                Count = count,
                TotalObtained = total,
                FirstObtained = FromMs(Long(item, "GetDatetime")),
                LastObtained = FromMs(Long(item, "LastGetDatetime")),
            });
        }
        stats.Items = stats.Items.OrderByDescending(i => i.TotalObtained).ThenBy(i => i.Id).ToList();

        // Draws: pulls per gacha come from step groups; crystals are estimated by walking the step sequence.
        var draws = new Dictionary<long, EosDrawStat>();
        foreach (var group in List(info, "UserGachaStepGroupList"))
        {
            var groupId = Long(group, "GachaStepGroupId");
            if (!m.StepGroupGacha.TryGetValue(groupId, out var gachaId))
            {
                stats.Warnings.Add($"Unknown draw step group {groupId} (master data may be out of date).");
                continue;
            }
            var draw = GetDraw(draws, gachaId, m);
            draw.TotalPulls += Long(group, "TotalDrawCount");
            var lastPulled = FromMs(Long(group, "LastExecDatetime"));
            if (lastPulled > draw.LastPulled || draw.LastPulled is null) draw.LastPulled = lastPulled;

            if (m.StepsByGroup.TryGetValue(groupId, out var steps) && steps.Count > 0)
            {
                var bySeq = steps.GroupBy(s => s.Seq).ToDictionary(g => g.Key, g => g.First());
                var seq = steps[0].Seq;
                var execs = Long(group, "TotalExecCount");
                for (long i = 0; i < execs; i++)
                {
                    var step = bySeq.TryGetValue(seq, out var s) ? s : steps[0];
                    if (CrystalConsumptionTypes.Contains(step.ConsumptionType))
                    {
                        draw.EstimatedCrystalsUsed += step.ConsumptionCount;
                        draw.CrystalPulls += step.DrawCount;
                    }
                    seq = step.NextSeq;
                }
            }
        }

        foreach (var sheet in List(info, "UserGachaStampSheetGroupList"))
        {
            var id = Long(sheet, "GachaStampSheetGroupId");
            var draw = GetDraw(draws, id, m);
            draw.TotalStampPages += (int)Long(sheet, "CompleteSheetCount");
            draw.TotalStamps += (int)Long(sheet, "TotalStampCount");
        }

        stats.Draws = draws.Values.OrderBy(d => d.Id).ToList();
        stats.TotalPulls = stats.Draws.Sum(d => d.TotalPulls);
        stats.TotalCrystalPulls = stats.Draws.Sum(d => d.CrystalPulls);
        stats.TotalFreePulls = stats.TotalPulls - stats.TotalCrystalPulls;
        stats.EstimatedCrystalsUsedForPulls = stats.Draws.Sum(d => d.EstimatedCrystalsUsed);

        stats.TotalHomeBackgroundsOwned = List(info, "UserHomeBackgroundList").Count();
        stats.TotalCrystalAdsViewed = List(info, "UserAdvertisementList").Sum(a => Long(a, "TotalViewCount"));

        // Highwind treasure battles: direction type 0 = Cactuar, 1 = Gold Cactuar, 2 = Gold Bomb.
        foreach (var battle in List(info, "UserHighwindBattleList"))
        {
            if (!m.HighwindBattles.TryGetValue(Long(battle, "HighwindBattleId"), out var hb) || hb.Type != 2) continue;
            var wins = Long(battle, "WinCount");
            switch (hb.DirectionType)
            {
                case 0: stats.TotalHighwindCactuars += wins; break;
                case 1: stats.TotalHighwindGoldCactuars += wins; break;
                case 2: stats.TotalHighwindGoldBombs += wins; break;
            }
        }

        // Weapon voucher exchanges per weapon, from the voucher shop purchase counts.
        var voucherBuys = new Dictionary<long, (long Count, long Last)>();
        foreach (var p in List(info, "UserShopItemList"))
        {
            var count = Long(p, "TotalPurchaseCount");
            if (count <= 0 || !m.VoucherShopItems.TryGetValue(Long(p, "ShopItemId"), out var vw)) continue;
            foreach (var wid in vw)
            {
                var cur = voucherBuys.GetValueOrDefault(wid);
                voucherBuys[wid] = (cur.Count + count, Math.Max(cur.Last, Long(p, "LastPurchaseDatetime")));
            }
        }
        foreach (var weapon in List(info, "UserWeaponList"))
        {
            var id = Long(weapon, "WeaponId");
            m.Weapons.TryGetValue(id, out var w);
            // Rarity 1/2/3 = 3★/4★/5★, 101 = 6★ ultimate. Upgrade type 1 = overboost 1-10, type 2 = the +1..+20 after OB10.
            var rarity = (int)Long(weapon, "RarityType");
            var upgradeType = Long(weapon, "WeaponUpgradeType");
            var upgrades = Long(weapon, "UpgradeCount");
            int level = 0, maxLevel = 0;
            if (m.WeaponGrowth.TryGetValue(id, out var growth))
            {
                var levels = m.WeaponLevelExp.GetValueOrDefault(growth.LevelGroup);
                var limits = m.WeaponLevelLimits.GetValueOrDefault(growth.ReleaseGroup);
                // Exp needed for a level = ExpCoefficient × BaseExp / 1000.
                if (levels is not null)
                    level = levels.LastOrDefault(l => l.Coefficient * growth.BaseExp / 1000 <= Long(weapon, "Exp")).Level;
                if (limits is not null)
                {
                    if (limits.TryGetValue((int)Long(weapon, "ReleaseCount"), out var cap)) level = Math.Min(level, cap);
                    if (m.WeaponMaxRelease.TryGetValue((growth.RarityGroup, rarity), out var maxRelease) && limits.TryGetValue(maxRelease, out var max)) maxLevel = max;
                }
            }
            stats.Weapons.Add(new EosWeaponStat
            {
                Id = id,
                Name = w?.Name ?? $"Weapon #{id}",
                Character = w?.Character ?? string.Empty,
                GachaPulls = (int)Long(weapon, "ObtainCountFromGacha"),
                Stars = rarity switch { 1 => 3, 2 => 4, 3 => 5, 101 => 6, _ => 0 },
                Level = level,
                MaxLevel = maxLevel,
                Overboost = upgradeType == 2 ? 10 : upgradeType == 1 ? (int)upgrades : 0,
                OverboostPlus = upgradeType == 2 ? (int)upgrades : 0,
                FirstObtained = FromMs(Long(weapon, "GetDatetime")),
                VoucherExchanges = voucherBuys.GetValueOrDefault(id).Count,
                LastVoucherExchange = FromMs(voucherBuys.GetValueOrDefault(id).Last),
            });
        }
        // Weapons in the game data the player doesn't have.
        var ownedWeaponIds = stats.Weapons.Select(w => w.Id).ToHashSet();
        foreach (var (id, w) in m.Weapons.Where(kv => !ownedWeaponIds.Contains(kv.Key) && m.WeaponCharacter.ContainsKey(kv.Key)))
            stats.Weapons.Add(new EosWeaponStat { Id = id, Name = w.Name, Character = w.Character, Owned = false });
        stats.Weapons = stats.Weapons.OrderByDescending(w => w.GachaPulls).ThenBy(w => w.Name).ToList();
        // Weapon parts: each weapon has its own "<name> Parts" item (Weapon.WeaponMedalItemId).
        var itemCounts = stats.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => (Count: g.Sum(i => i.Count), Obtained: g.Sum(i => i.TotalObtained)));
        foreach (var w in stats.Weapons)
            if (m.WeaponParts.TryGetValue(w.Id, out var partsItem)) { w.Parts = itemCounts.GetValueOrDefault(partsItem).Count; w.PartsObtained = itemCounts.GetValueOrDefault(partsItem).Obtained; }

        var ownedCostumes = List(info, "UserCharacterCostumeList").Select(c => Long(c, "CostumeId")).ToHashSet();
        stats.Outfits = m.CostumeCharacter
            .Select(kv => new EosOutfitStat
            {
                Name = m.CostumeNames.GetValueOrDefault(kv.Key) is { Length: > 0 } n ? n : $"Outfit #{kv.Key}",
                Character = CharacterName(m, kv.Value),
                Owned = ownedCostumes.Contains(kv.Key),
            })
            .OrderBy(o => m.Characters.Values.FirstOrDefault(v => v.Name == o.Character).Order).ThenBy(o => o.Name)
            .ToList();

        foreach (var recipe in List(info, "UserMateriaRecipeList"))
        {
            var id = Long(recipe, "MateriaRecipeId");
            stats.MateriaRecipes.Add(new EosMateriaRecipeStat
            {
                Id = id,
                Name = m.RecipeNames.TryGetValue(id, out var n) ? n : $"Recipe #{id}",
                TotalCrafted = Long(recipe, "CraftCount"),
            });
        }
        stats.MateriaRecipes = stats.MateriaRecipes.OrderByDescending(r => r.TotalCrafted).ToList();

        string[] quality = { "One", "Two", "Three", "Four", "Five" };
        foreach (var c in List(info, "UserMateriaCollectionList"))
        {
            for (var q = 0; q < 5; q++)
            {
                stats.Materia.ObtainedByStar[q] += Long(c, $"Quality{quality[q]}ObtainCount");
                stats.Materia.CraftedByStar[q] += Long(c, $"Quality{quality[q]}CraftCount");
            }
        }

        ParseAccountExtras(info, stats, m);
        ParseCollections(info, stats, m);
        ParseProgress(info, stats, m);
        if (includePurchases) stats.Purchases = ParsePurchases(info, m);

        stats.Warnings = stats.Warnings.Distinct().Take(10).ToList();
        return stats;
    }

    private static DateTime? FromMs(long ms) => ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;

    private static void ParseAccountExtras(JsonElement info, EosPlayerStats stats, MasterData m)
    {
        // Player rank from total exp; account creation is UserTimeType 1.
        var status = List(info, "UserStatusList").FirstOrDefault();
        if (status.ValueKind == JsonValueKind.Object)
            stats.PlayerRank = MasterData.LevelFor(m.UserRankExp, Long(status, "Exp"));
        stats.MaxPlayerRank = m.UserRankExp.Count > 0 ? m.UserRankExp[^1].Rank : 0;
        stats.AccountCreated = FromMs(List(info, "UserTimeList").Where(t => Long(t, "UserTimeType") == 1).Select(t => Long(t, "Datetime")).FirstOrDefault());

        var guildExp = List(info, "SharedGuildExpList").FirstOrDefault();
        if (guildExp.ValueKind == JsonValueKind.Object && m.GuildLevelExp.Count > 0)
            stats.GuildLevel = MasterData.LevelFor(m.GuildLevelExp, Long(guildExp, "LevelExp"));
        stats.GuildJoined = FromMs(List(info, "SharedGuildMemberList").Select(g => Long(g, "JoinDatetime")).FirstOrDefault());

        // Characters: level, costumes and weapons owned vs. what exists.
        var costumesOwned = List(info, "UserCharacterCostumeList").Select(c => Long(c, "CostumeId")).ToHashSet();
        var weaponsOwned = List(info, "UserWeaponList").ToDictionary(w => Long(w, "WeaponId"), w => Long(w, "ObtainCountFromGacha"));
        foreach (var c in List(info, "UserCharacterList"))
        {
            var id = Long(c, "CharacterId");
            m.CharacterLevelExp.TryGetValue(id, out var levels);
            var costumes = m.CostumeCharacter.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList();
            var weapons = m.WeaponCharacter.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList();
            stats.Characters.Add(new EosCharacterStat
            {
                Name = m.Characters.TryGetValue(id, out var ch) ? ch.Name : $"Character #{id}",
                Level = levels is null ? 0 : MasterData.LevelFor(levels, Long(c, "Exp")),
                MaxLevel = levels is null || levels.Count == 0 ? 0 : levels[^1].Level,
                CostumesOwned = costumes.Count(costumesOwned.Contains),
                CostumesTotal = costumes.Count,
                WeaponsOwned = weapons.Count(weaponsOwned.ContainsKey),
                WeaponsTotal = weapons.Count,
                WeaponPulls = weapons.Sum(w => weaponsOwned.GetValueOrDefault(w)),
            });
        }
        stats.Characters = stats.Characters
            .OrderBy(c => m.Characters.Values.FirstOrDefault(v => v.Name == c.Name).Order)
            .ToList();

        EosCollectionStat Coll(string name, int owned, int total) => new() { Name = name, Owned = owned, Total = total };
        stats.Collection = new List<EosCollectionStat>
        {
            Coll("Weapons", weaponsOwned.Count, m.WeaponCharacter.Count),
            Coll("Outfits", costumesOwned.Count, m.CostumeCharacter.Count),
            Coll("Memoria", List(info, "UserMemoriaList").Count(x => Long(x, "EquipableDatetime") > 0), m.TableCounts.GetValueOrDefault("Memoria")),
            Coll("Summons", List(info, "UserSummonList").Count(), m.TableCounts.GetValueOrDefault("Summon")),
            Coll("Home backgrounds", stats.TotalHomeBackgroundsOwned, m.TableCounts.GetValueOrDefault("HomeBackground")),
            Coll("Titles & profile items", List(info, "UserHonorList").Count(), m.TableCounts.GetValueOrDefault("Honor")),
        }.Where(c => c.Total > 0).ToList();

        // In-game profile play records, grouped like the profile screen.
        var records = new Dictionary<long, EosRecordGroup>();
        foreach (var r in List(info, "UserPlayRecordList")
                     .Select(r => (Type: Long(r, "PlayRecordType"), Value: Long(r, "IntValue")))
                     .Where(r => m.ProfileRecords.ContainsKey(r.Type))
                     .OrderBy(r => m.ProfileRecords[r.Type].Order))
        {
            var def = m.ProfileRecords[r.Type];
            var groupId = def.GroupId;
            if (groupId == 0)
            {
                // Ungrouped record = "Current Max. Power", shown in the header instead.
                if (r.Type == 4) stats.MaxPower = r.Value;
                continue;
            }
            // Group 5 repeats the Max. Damage Challenge scores, which get their own table on the Challenges tab.
            if (groupId == 5) continue;
            if (!records.TryGetValue(groupId, out var group))
            {
                records[groupId] = group = new EosRecordGroup
                {
                    Name = m.RecordGroups.TryGetValue(groupId, out var g) && g.Name.Length > 0 ? g.Name : "Overview",
                };
            }
            group.Records.Add(new EosNamedCount { Name = def.Name, Value = r.Value });
        }
        stats.RecordGroups = records
            .OrderBy(kv => m.RecordGroups.TryGetValue(kv.Key, out var g) ? g.Order : -1)
            .Select(kv => kv.Value)
            .ToList();

        // Crisis Battle and Damage Ranking events share one list; the damage ones are listed in EventDamageRankingBattle.
        var damageTeams = List(info, "UserEventDamageRankingBattleSoloBattleList")
            .Where(b => Long(b, "HighScoreCharacterId0") > 0)
            .GroupBy(b => Long(b, "EventSoloBattleId") / 1000)
            .ToDictionary(g => g.Key, g =>
            {
                var best = g.OrderByDescending(b => Long(b, "LastUpdateHighScoreDatetime")).First();
                var names = new[] { "HighScoreCharacterId0", "HighScoreCharacterId1", "HighScoreCharacterId2" }
                    .Select(k => Long(best, k)).Where(id => id > 0)
                    .Select(id => m.Characters.TryGetValue(id, out var c) ? c.Name : $"#{id}");
                return string.Join(", ", names);
            });
        // Per-stage bests; the event id is the stage id / 1000.
        // Stage scores live in the general solo battle list.
        var stageScores = List(info, "UserEventSoloBattleList").GroupBy(b => Long(b, "EventSoloBattleId"))
            .ToDictionary(g => g.Key, g => (Score: Long(g.First(), "HighScore"), Wins: Long(g.First(), "TotalWinCount")));
        List<EosEventStage> StagesFor(string list, long eventId) => List(info, list)
            .Where(b => Long(b, "EventSoloBattleId") / 1000 == eventId)
            .OrderBy(b => Long(b, "EventSoloBattleId"))
            .Select(b =>
            {
                var team = new[] { "HighScoreCharacterId0", "HighScoreCharacterId1", "HighScoreCharacterId2" }
                    .Select(k => Long(b, k)).Where(id => id > 0).Select(id => CharacterName(m, id)).ToList();
                var modifiers = 0;
                var levelBonus = 0;
                var raw = Str(b, "BuffDebuffSelectionInfo");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(raw);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            foreach (var p in doc.RootElement.EnumerateObject())
                                if (p.Value.ValueKind == JsonValueKind.Array)
                                    foreach (var mod in p.Value.EnumerateArray())
                                    {
                                        modifiers++;
                                        if (mod.TryGetInt64(out var modId)) levelBonus += m.CrisisModifierLevels.GetValueOrDefault(modId);
                                    }
                    }
                    catch (JsonException) { }
                }
                var id = Long(b, "EventSoloBattleId");
                var name = m.SoloBattleNames.GetValueOrDefault(id);
                return new EosEventStage
                {
                    Name = string.IsNullOrEmpty(name) ? $"Stage #{id}" : name,
                    Team = team.Count > 0 ? string.Join(", ", team) : null,
                    BestSet = FromMs(Long(b, "LastUpdateHighScoreDatetime")),
                    WithMemoria = b.TryGetProperty("IsHighScoreWithMemoria0", out var mem) && mem.ValueKind == JsonValueKind.True,
                    Modifiers = modifiers,
                    HighScore = stageScores.TryGetValue(id, out var sc) ? sc.Score : 0,
                    Clears = stageScores.TryGetValue(id, out var sw) ? sw.Wins : 0,
                    StageLevel = m.CrisisStageDefaults.TryGetValue(id, out var baseLevel) ? baseLevel + levelBonus : null,
                };
            }).ToList();
        foreach (var e in List(info, "UserEventCrisisBattleList"))
        {
            var id = Long(e, "EventCrisisBattleId");
            m.CrisisEvents.TryGetValue(id, out var d);
            var rank = new EosEventRank
            {
                Name = string.IsNullOrEmpty(d.Name) ? $"Event #{id}" : d.Name,
                EventBaseId = d.EventBaseId,
                EndDate = FromMs(d.EndMs),
                FinalRank = (int)Long(e, "FinalRank"),
                Points = Long(e, "TotalPoint"),
                HighStage = (int)Long(e, "HighStageLevel"),
            };
            if (m.DamageRankingEvents.Contains(id))
            {
                rank.BestDamage = Long(e, "MaxHighScore");
                rank.BestTeam = damageTeams.GetValueOrDefault(id);
                rank.Stages = StagesFor("UserEventDamageRankingBattleSoloBattleList", id);
                stats.DamageRankings.Add(rank);
            }
            else
            {
                rank.Stages = StagesFor("UserEventCrisisBattleSoloBattleList", id);
                stats.CrisisEvents.Add(rank);
            }
        }
        stats.CrisisEvents = stats.CrisisEvents.OrderByDescending(e => e.EndDate).ToList();
        stats.DamageRankings = stats.DamageRankings.OrderByDescending(e => e.EndDate).ToList();

        // Guild battles: the player's own row carries the rank of the guild they were in at the time;
        // the shared rows are the current guild's history.
        var currentGuildRanks = List(info, "SharedGuildEventGuildRankingBaseList")
            .GroupBy(g => Long(g, "EventBaseId"))
            .ToDictionary(g => g.Key, g => (int)Long(g.First(), "FinalTotalScoreRank"));
        // Shared rows are the current guild's history, including battles fought before the player joined.
        // Only trust them where the player's own final rank matches, i.e. they were in this guild for that battle.
        var ownGuildRanks = List(info, "UserEventGuildRankingBaseList")
            .GroupBy(g => Long(g, "EventBaseId")).ToDictionary(g => g.Key, g => (int)Long(g.First(), "FinalTotalScoreRank"));
        bool SameGuild(long eventBaseId) => currentGuildRanks.TryGetValue(eventBaseId, out var cur) && cur > 0
            && ownGuildRanks.TryGetValue(eventBaseId, out var own) && own == cur;
        var guildShared = List(info, "SharedGuildEventGuildRankingBattleList")
            .GroupBy(g => Long(g, "EventGuildRankingBattleId")).ToDictionary(g => g.Key, g => g.First());
        var fightsByEvent = List(info, "UserEventGuildRankingBattleList")
            .Select(f => (Id: Long(f, "EventGuildRankingBattleId"), Row: f))
            .GroupBy(f => m.GuildFights.TryGetValue(f.Id, out var d) ? d.EventBaseId : f.Id / 100)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Id).Select(f =>
            {
                m.GuildFights.TryGetValue(f.Id, out var d);
                JsonElement sh = default;
                var hasShared = SameGuild(d.EventBaseId) && guildShared.TryGetValue(f.Id, out sh);
                return new EosGuildFight
                {
                    Stars = d.Stars,
                    Boss = string.IsNullOrEmpty(d.Boss) ? $"Boss #{f.Id}" : d.Boss,
                    HighScore = Long(f.Row, "HighScore"),
                    PracticeBest = Long(f.Row, "PracticeBattleBestScore"),
                    GuildDefeats = hasShared ? Long(sh, "EnemyDefeatCount") : null,
                    GuildScore = hasShared ? Long(sh, "TotalScore") : null,
                };
            }).ToList());
        var guildBattleNames = new Dictionary<string, int>();
        stats.GuildBattles = List(info, "UserEventGuildRankingBaseList")
            .Select(g => (BaseId: Long(g, "EventBaseId"), Rank: (int)Long(g, "FinalTotalScoreRank")))
            .OrderBy(g => m.GuildBattles.TryGetValue(g.BaseId, out var d) ? d.EndMs : 0)
            .Select(g =>
            {
                m.GuildBattles.TryGetValue(g.BaseId, out var d);
                var name = string.IsNullOrEmpty(d.Name) ? $"Guild Battle ({g.BaseId})" : d.Name;
                // Some battles ran twice under one name; number the repeats.
                guildBattleNames[name] = guildBattleNames.GetValueOrDefault(name) + 1;
                if (guildBattleNames[name] > 1) name += $" (part {guildBattleNames[name]})";
                return new EosEventRank
                {
                    Name = name,
                    EventBaseId = g.BaseId,
                    EndDate = FromMs(d.EndMs),
                    FinalRank = g.Rank,
                    CurrentGuildRank = currentGuildRanks.TryGetValue(g.BaseId, out var cr) && cr > 0 ? cr : null,
                    Fights = fightsByEvent.GetValueOrDefault(g.BaseId) ?? new(),
                };
            })
            .OrderByDescending(g => g.EndDate)
            .ToList();

        stats.ScoreDungeons = List(info, "UserEventScoreDungeonEntryList").Select(e =>
        {
            m.ScoreDungeons.TryGetValue(Long(e, "EventScoreDungeonId"), out var d);
            return new EosEventRank
            {
                Name = string.IsNullOrEmpty(d.Name) ? $"Dungeon #{Long(e, "EventScoreDungeonId")}" : d.Name,
                EventBaseId = d.EventBaseId,
                EndDate = FromMs(d.EndMs),
                FinalRank = (int)Long(e, "FinalRank"),
            };
        }).OrderByDescending(e => e.EndDate).ToList();

        // Badges (called "awards" in the data). Ranking badges also list the placements that earned them.
        var placements = stats.CrisisEvents.Concat(stats.DamageRankings).Concat(stats.ScoreDungeons).Concat(stats.GuildBattles)
            .Where(p => p.EventBaseId > 0 && p.FinalRank > 0)
            .GroupBy(p => p.EventBaseId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.FinalRank).First());
        var awardCounts = List(info, "UserAwardList").ToDictionary(a => Long(a, "AwardId"), a => Long(a, "AwardCount"));
        foreach (var (awardId, def) in m.Awards.OrderBy(a => a.Value.Order))
        {
            var badge = new EosBadge
            {
                Name = def.Name,
                Description = def.Description,
                Count = awardCounts.GetValueOrDefault(awardId),
            };
            var earned = new List<EosEventRank>();
            foreach (var (baseId, maxRank) in m.AwardRankings.GetValueOrDefault(awardId) ?? new())
            {
                if (!placements.TryGetValue(baseId, out var p) || p.FinalRank > maxRank) continue;
                p.EarnedBadge = true;
                earned.Add(p);
            }
            badge.EarnedFrom = earned.OrderByDescending(p => p.EndDate).Select(p => $"{p.Name} (#{p.FinalRank})").ToList();
            if (badge.Count > 0 || badge.EarnedFrom.Count > 0) stats.Badges.Add(badge);
        }

        stats.Towers = List(info, "UserTowerList").Select(t =>
        {
            m.Towers.TryGetValue(Long(t, "TowerId"), out var d);
            return new EosProgressStat { Name = d.Name ?? $"Tower #{Long(t, "TowerId")}", Cleared = (int)Long(t, "ClearFloorSeq"), Total = d.Floors };
        }).OrderBy(t => t.Name).ToList();

        // Battle Tower: Singularity events: each floor is an event solo battle (id = event id × 1000 + floor).
        var soloWins = List(info, "UserEventSoloBattleList").Where(b => Long(b, "TotalWinCount") > 0).Select(b => Long(b, "EventSoloBattleId") / 1000)
            .GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
        stats.SingularityTowers = m.SingularityTowers
            .Where(t => soloWins.ContainsKey(t.Key))
            .OrderBy(t => t.Key)
            .Select(t => new EosProgressStat { Name = t.Value.Name, Cleared = soloWins[t.Key], Total = t.Value.Floors })
            .ToList();

        stats.DamageChallenges = List(info, "UserDamageChallengeBattleList")
            .Select(d => (Id: Long(d, "DamageChallengeBattleId"), Score: Long(d, "HighScore"), Tries: Long(d, "TotalEntryCount"), Updated: Long(d, "LastUpdateScoreDatetime")))
            .OrderBy(d => m.DamageChallenges.TryGetValue(d.Id, out var x) ? x.Order : int.MaxValue)
            .Select(d => new EosDamageChallengeStat
            {
                Name = m.DamageChallenges.TryGetValue(d.Id, out var x) ? x.Name : $"Challenge #{d.Id}",
                HighScore = d.Score,
                Attempts = d.Tries,
                LastImproved = FromMs(d.Updated),
            }).ToList();
    }

    // Criterion dungeon rank letters, indexed by RankType (1 = F+ ... 14 = SS; localization ids 213116 + RankType).
    private static readonly string[] DungeonRanks = { "F", "F+", "E", "E+", "D", "D+", "C", "C+", "B", "B+", "A", "A+", "S", "S+", "SS" };

    private static string CharacterName(MasterData m, long id) =>
        m.Characters.TryGetValue(id, out var c) ? c.Name : $"#{id}";

    // Mission group types that reset daily/weekly, so "completed" means nothing for them.
    private static readonly HashSet<long> RepeatingMissionGroupTypes = new() { 2, 3, 11, 15, 16, 19, 9999 };

    private static void ParseProgress(JsonElement info, EosPlayerStats stats, MasterData m)
    {
        // Highwind
        stats.Highwind.IdleCollections = List(info, "UserHighwindList").Sum(h => Long(h, "TotalIdlingCollectCount"));
        stats.Highwind.Parts = List(info, "UserHighwindPartsList")
            .OrderBy(p => Long(p, "HighwindPartsId"))
            .Select(p => new EosNamedCount
            {
                Name = m.HighwindParts.GetValueOrDefault(Long(p, "HighwindPartsId")) ?? $"Part {Long(p, "HighwindPartsId")}",
                Value = Long(p, "Level"),
            }).ToList();
        stats.Highwind.KeyItemsTotal = m.HighwindKeyItems.Count;
        stats.Highwind.KeyItems = List(info, "UserHighwindKeyItemList").Select(k =>
        {
            m.HighwindKeyItems.TryGetValue(Long(k, "HighwindKeyItemId"), out var def);
            return new EosHighwindKeyItem
            {
                Name = string.IsNullOrEmpty(def.Name) ? $"Key item #{Long(k, "HighwindKeyItemId")}" : def.Name,
                Upgrades = (int)Long(k, "UpgradeCount"),
                MaxUpgrades = def.MaxUpgrades,
                Obtained = FromMs(Long(k, "GetDatetime")),
            };
        }).OrderBy(k => k.Obtained).ToList();

        // Guild
        stats.Guild.Created = List(info, "SharedGuildBaseList").Select(g => FromMs(Long(g, "CreatedDatetime"))).FirstOrDefault();
        stats.Guild.TimesLeftAGuild = (int)List(info, "UserGuildBaseList").Sum(g => Long(g, "TotalLeaveCount"));
        stats.Guild.Bonuses = List(info, "UserGuildBonusList")
            .OrderBy(b => m.GuildBonuses.TryGetValue(Long(b, "GuildBonusId"), out var d) ? d.Order : 99)
            .Select(b =>
            {
                m.GuildBonuses.TryGetValue(Long(b, "GuildBonusId"), out var d);
                var name = d.Name ?? $"Bonus #{Long(b, "GuildBonusId")}";
                stats.Guild.BonusMax[name] = d.MaxLevel;
                return new EosNamedCount { Name = name, Value = Long(b, "Level") };
            }).ToList();
        // Guild level achievements repeat once per reward track; keep one row per description.
        stats.Guild.Achievements = List(info, "SharedGuildAchievementList")
            .Select(a => (Def: m.GuildAchievements.GetValueOrDefault(Long(a, "GuildAchievementId")), Progress: Long(a, "ProgressCount")))
            .Where(a => a.Def.Text != null)
            .GroupBy(a => a.Def.Text)
            .Select(g => g.First())
            .OrderBy(a => a.Def.Order)
            .Select(a => new EosGuildAchievement { Description = a.Def.Text!, Progress = a.Progress, Goal = a.Def.Goal })
            .ToList();

        // Memoria: owned once enough fragments are collected (EquipableDatetime set).
        var userMemoria = List(info, "UserMemoriaList").GroupBy(x => Long(x, "MemoriaId")).ToDictionary(g => g.Key, g => g.First());
        stats.Memoria = m.Memoria.Select(kv =>
        {
            var has = userMemoria.TryGetValue(kv.Key, out var um);
            var equipable = has ? Long(um, "EquipableDatetime") : 0;
            var levels = m.MemoriaLevels.GetValueOrDefault(m.MemoriaLevelGroup.GetValueOrDefault(kv.Key)) ?? new();
            var points = has ? Long(um, "AnalysisPoint") : 0;
            return new EosMemoriaStat
            {
                Name = kv.Value.Name,
                Stars = kv.Value.Rarity,
                Source = kv.Value.Source,
                Owned = equipable > 0,
                Fragments = has ? (int)Long(um, "FragmentCount") : 0,
                FragmentsNeeded = kv.Value.Fragments,
                Obtained = FromMs(equipable),
                Level = equipable > 0 && levels.Count > 0 ? levels.Where(l => l.Threshold <= points).Select(l => l.Level).DefaultIfEmpty(1).Max() : null,
                MaxLevel = levels.Count > 0 ? levels.Max(l => l.Level) : 0,
            };
        }).OrderByDescending(x => x.Owned).ThenByDescending(x => x.Stars).ThenBy(x => x.Name).ToList();

        // Skills
        var special = List(info, "UserSkillSpecialList").Select(x => Long(x, "SpecialSkillId")).ToHashSet();
        var overaccel = List(info, "UserSkillOveraccelList").Select(x => Long(x, "OveraccelSkillId")).ToHashSet();
        stats.Skills = m.SpecialSkills.Select(kv => new EosSkillStat { Name = kv.Value.Name, Type = kv.Value.Type, For = kv.Value.For, Owned = special.Contains(kv.Key) })
            .Concat(m.OveraccelSkills.Select(kv => new EosSkillStat { Name = kv.Value.Name, Type = "Overaccel", For = kv.Value.For, Owned = overaccel.Contains(kv.Key) }))
            .ToList();

        // Missions: skip repeating daily/weekly groups; a mission is complete once its last reward step is claimed.
        stats.Missions = List(info, "UserMissionList").Select(u =>
        {
            if (!m.Missions.TryGetValue(Long(u, "MissionId"), out var def) || RepeatingMissionGroupTypes.Contains(def.GroupType)) return null;
            var progress = Long(u, "ProgressCount");
            var claimed = Long(u, "ReceivedProgressCount");
            var goal = def.Goal > 0 ? def.Goal : 1;
            return new EosMissionStat
            {
                Category = def.Category,
                Mission = def.Text.Replace("{0}", goal.ToString("N0")),
                Progress = Math.Min(progress, goal),
                Goal = goal,
                Complete = claimed >= goal,
            };
        }).Where(x => x != null && x.Mission.Length > 0).Select(x => x!)
            .OrderBy(x => x.Category).ThenBy(x => x.Mission).ToList();

        ParseBattles(info, stats, m);
        ParseGrowthAndShops(info, stats, m);
    }

    private static void ParseGrowthAndShops(JsonElement info, EosPlayerStats stats, MasterData m)
    {
        // Growth boards: GroupIdxFlags0 holds node idx 0-63 as a 64-bit mask, GroupIdxFlags1 idx 64+.
        static ulong Mask(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
                ? (v.TryGetUInt64(out var u) ? u : v.TryGetInt64(out var l) ? unchecked((ulong)l) : 0UL) : 0UL;
        var unlocked = new Dictionary<long, (ulong F0, ulong F1)>();
        foreach (var g in List(info, "UserGrowthBoardGroupList"))
            unlocked[Long(g, "GrowthBoardGroupId")] = (Mask(g, "GroupIdxFlags0"), Mask(g, "GroupIdxFlags1"));
        var boards = new Dictionary<(int Type, long Target), EosGrowthBoard>();
        foreach (var (groupId, def) in m.GrowthGroups)
        {
            if (!m.GrowthNodes.TryGetValue(groupId, out var nodes)) continue;
            var key = (def.Type, def.Target);
            if (!boards.TryGetValue(key, out var b))
            {
                var (label, name) = def.Type switch
                {
                    1 => ("Character", m.Characters.TryGetValue(def.Target, out var c) ? c.Name : null),
                    5 => ("Character overaccel", m.Characters.TryGetValue(def.Target, out var c2) ? c2.Name : null),
                    4 => ("Summon", m.SummonNames.GetValueOrDefault(def.Target)),
                    6 => ("Enemy ability", m.AbilityEnemyNames.GetValueOrDefault(def.Target)),
                    _ => ("Other", null),
                };
                boards[key] = b = new EosGrowthBoard { Type = label, Name = string.IsNullOrWhiteSpace(name) ? $"#{def.Target}" : name };
            }
            b.Boards++;
            var flags = unlocked.GetValueOrDefault(groupId);
            foreach (var n in nodes)
            {
                b.NodesTotal++;
                var on = n.Idx < 64 ? (flags.F0 >> n.Idx & 1) == 1 : n.Idx < 128 && (flags.F1 >> (n.Idx - 64) & 1) == 1;
                if (!on) continue;
                b.NodesUnlocked++;
                if (n.Flat)
                {
                    b.Hp += n.Hp; b.PhysicalAttack += n.PAtk; b.MagicalAttack += n.MAtk;
                    b.PhysicalDefense += n.PDef; b.MagicalDefense += n.MDef; b.Healing += n.Heal;
                }
            }
        }
        stats.GrowthBoards = boards.Values.Where(b => b.NodesUnlocked > 0 || b.Type is "Character" or "Summon")
            .OrderBy(b => b.Type).ThenByDescending(b => b.NodesUnlocked).ThenBy(b => b.Name).ToList();

        // Shop exchanges, excluding real-money packs (those stay behind the "Show purchases" option).
        foreach (var s in List(info, "UserShopItemList"))
        {
            var count = Long(s, "TotalPurchaseCount");
            if (count <= 0 || !m.ShopItems.TryGetValue(Long(s, "ShopItemId"), out var item) || item.StoreGroupId > 0) continue;
            var shop = m.ShopNames.GetValueOrDefault(item.ShopId) ?? "Shop";
            stats.ShopExchanges.Add(new EosShopExchange
            {
                Name = string.IsNullOrWhiteSpace(item.Name) ? $"Shop item #{Long(s, "ShopItemId")}" : item.Name,
                Shop = shop,
                Count = count,
                LastPurchased = FromMs(Long(s, "LastPurchaseDatetime")),
            });
        }
        stats.ShopExchanges = stats.ShopExchanges.OrderByDescending(x => x.LastPurchased).ToList();

        // Wishlists: each saved choice is a (slot, weapon) on a GachaWish shared by one or more draws.
        var pulls = stats.Draws.ToDictionary(d => d.Id, d => d.LastPulled);
        var wishes = new Dictionary<long, EosWishlist>();
        foreach (var c in List(info, "UserGachaWishChoiceList"))
        {
            if (!m.WishChoices.TryGetValue(Long(c, "GachaWishChoiceId"), out var choice)) continue;
            if (!wishes.TryGetValue(choice.WishId, out var w))
            {
                var gachas = m.WishGachas.GetValueOrDefault(choice.WishId) ?? new List<long>();
                wishes[choice.WishId] = w = new EosWishlist
                {
                    Id = choice.WishId,
                    Banners = string.Join(" / ", gachas.Select(g => m.Gachas.TryGetValue(g, out var gi) ? gi.Name.Replace("\n", " ") : null)
                        .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()),
                    LastPulled = gachas.Select(g => pulls.GetValueOrDefault(g)).Where(d => d is not null).DefaultIfEmpty(null).Max(),
                };
            }
            var weaponId = Long(c, "WeaponId");
            var weapon = m.Weapons.GetValueOrDefault(weaponId);
            w.Picks.Add(new EosWishPick
            {
                Slot = choice.Order,
                Weapon = weapon?.Name ?? $"Weapon #{weaponId}",
                Character = weapon?.Character ?? string.Empty,
                Changed = weaponId != choice.DefaultWeapon,
            });
        }
        foreach (var w in wishes.Values) w.Picks = w.Picks.OrderBy(p => p.Slot).ToList();
        stats.Wishlists = wishes.Values.OrderByDescending(w => w.LastPulled).ThenByDescending(w => w.Id).ToList();

        // Event box draws ("Victory Draw"): tickets used / cost per draw = draws; each box reset = a box emptied.
        var itemUse = List(info, "UserItemList").ToDictionary(i => Long(i, "ItemId"), i => (Used: Long(i, "TotalConsumptionCount"), Last: Long(i, "LastGetDatetime")));
        foreach (var u in List(info, "UserBoxGachaList"))
        {
            if (!m.BoxGachas.TryGetValue(Long(u, "BoxGachaId"), out var box)) continue;
            var eventId = m.BoxGroupEvent.GetValueOrDefault(box.GroupId);
            var use = itemUse.GetValueOrDefault(box.CostItem);
            stats.BoxDraws.Add(new EosBoxDraw
            {
                EventId = eventId,
                Event = m.EventNames.GetValueOrDefault(eventId) ?? $"Event #{eventId}",
                Box = box.Name,
                BoxesReset = Long(u, "TotalRewardGroupUpdateCount"),
                Draws = box.CostCount > 0 ? use.Used / box.CostCount : 0,
                Ticket = m.ItemNames.GetValueOrDefault(box.CostItem) ?? $"Item #{box.CostItem}",
                TicketsUsed = use.Used,
                LastTicket = FromMs(use.Last),
            });
        }
        stats.BoxDraws = stats.BoxDraws.OrderByDescending(b => b.EventId).ThenBy(b => b.Box).ToList();

        // Logins: LoginBonusType 1 = the regular daily bonus (one track per year); other types are limited campaigns.
        foreach (var lb in List(info, "UserLoginBonusList"))
        {
            var total = Long(lb, "TotalLoginCount");
            if (total <= 0) continue;
            if (m.LoginBonusTypes.GetValueOrDefault(Long(lb, "LoginBonusId")) == 1)
            {
                stats.Logins.DaysLoggedIn += total;
                var last = FromMs(Long(lb, "LastGetDate"));
                if (last > stats.Logins.LastLogin || stats.Logins.LastLogin is null) stats.Logins.LastLogin = last;
            }
            else
            {
                stats.Logins.Campaigns++;
                stats.Logins.CampaignLogins += total;
            }
        }
        if (stats.AccountCreated is DateTime created && stats.Logins.LastLogin is DateTime lastLogin)
            stats.Logins.DaysAvailable = (long)(lastLogin.Date - created.Date).TotalDays + 1;

        var meet = List(info, "UserFirstMeetingList").FirstOrDefault();
        if (meet.ValueKind == JsonValueKind.Object) stats.CoopPlayersMet = Long(meet, "Count");

        // Limited-time offers: LimitedRelease rows unlock a shop item (priced in red crystals) for a few hours.
        var purchases = List(info, "UserShopItemList").ToDictionary(s => Long(s, "ShopItemId"), s => Long(s, "TotalPurchaseCount"));
        stats.LimitedOffers = List(info, "UserLimitedReleaseList")
            .Select(u => (Item: m.LimitedReleaseShopItem.GetValueOrDefault(Long(u, "LimitedReleaseId")), At: FromMs(Long(u, "ReleaseStartDatetime")),
                Name: m.LimitedReleaseNames.GetValueOrDefault(Long(u, "LimitedReleaseId"))))
            .Where(x => x.Item > 0)
            .GroupBy(x => x.Item)
            .Select(g => new EosLimitedOffer
            {
                // The offer's own name is filled in more often than the shop item's.
                Name = (g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? (m.ShopItems.TryGetValue(g.Key, out var si) && !string.IsNullOrWhiteSpace(si.Name) ? si.Name : $"Pack #{g.Key}")).Replace("\n", " "),
                FirstOffered = g.Min(x => x.At),
                LastOffered = g.Max(x => x.At),
                TimesOffered = g.Count(),
                PriceCrystals = m.ShopItemCrystalPrice.GetValueOrDefault(g.Key),
                Bought = purchases.GetValueOrDefault(g.Key),
            })
            .OrderByDescending(o => o.FirstOffered).ToList();

        // Paint Cans (Highwind currency) are a "big item": significand × 10^exponent.
        foreach (var bi in List(info, "UserBigItemList"))
            if (Long(bi, "BigItemId") == 18001)
                stats.PaintCans = Long(bi, "CountSignificand") * (long)Math.Pow(10, Long(bi, "CountExponent"));
    }

    private static void ParseBattles(JsonElement info, EosPlayerStats stats, MasterData m)
    {
        var bs = stats.Battles;
        var wins = new List<(EosBattleWin Win, long EventId, string AreaKey, string Category)>();
        string Or(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name;

        foreach (var b in List(info, "UserEventSoloBattleList"))
        {
            var id = Long(b, "EventSoloBattleId");
            var ev = m.SoloBattleEvent.TryGetValue(id, out var e) ? e : id / 1000;
            var evName = Or(m.EventNames.GetValueOrDefault(ev), $"Event #{ev}");
            wins.Add((new EosBattleWin { Name = Or(m.SoloBattleNames.GetValueOrDefault(id), $"Battle #{id}"), Source = evName, Wins = Long(b, "TotalWinCount"), HighScore = Long(b, "HighScore") }, ev, "", ""));
        }
        foreach (var b in List(info, "UserEventMultiBattleList"))
        {
            var id = Long(b, "EventMultiBattleId");
            var ev = m.MultiBattleEvent.GetValueOrDefault(id);
            var evName = Or(m.EventNames.GetValueOrDefault(ev), $"Event #{ev}");
            wins.Add((new EosBattleWin { Name = Or(m.MultiBattleNames.GetValueOrDefault(id), $"Battle #{id}"), Source = evName, Coop = true, Wins = Long(b, "TotalWinCount"), HighScore = Long(b, "HighScore") }, ev, "", ""));
        }
        foreach (var b in List(info, "UserSoloAreaBattleList"))
        {
            var id = Long(b, "SoloAreaBattleId");
            m.SoloAreaBattles.TryGetValue(id, out var d);
            var area = Or(d.Area, $"Area #{id / 100}");
            wins.Add((new EosBattleWin { Name = Or(d.Battle, $"Battle #{id}"), Source = area, Wins = Long(b, "TotalWinCount"), HighScore = Long(b, "HighScore") }, 0, "S" + area, "Solo area"));
        }
        foreach (var b in List(info, "UserMultiAreaBattleList"))
        {
            var id = Long(b, "MultiAreaBattleId");
            m.MultiAreaBattles.TryGetValue(id, out var d);
            var area = Or(d.Area, $"Boss #{id / 100}");
            wins.Add((new EosBattleWin { Name = Or(d.Battle, $"Battle #{id}"), Source = area, Coop = true, Wins = Long(b, "TotalWinCount"), HighScore = Long(b, "HighScore") }, 0, "M" + area, Or(d.Label, "Co-op") + " co-op boss"));
        }

        var events = wins.Where(w => w.EventId > 0).ToList();
        var areas = wins.Where(w => w.AreaKey.Length > 0).ToList();
        var criterion = List(info, "UserAnotherBattleList").ToList();
        var highwind = stats.TotalHighwindCactuars + stats.TotalHighwindGoldCactuars + stats.TotalHighwindGoldBombs;
        bs.ByMode = new List<EosBattleMode>
        {
            new() { Name = "Event battles", Wins = events.Where(w => !w.Win.Coop).Sum(w => w.Win.Wins), Battles = events.Count(w => !w.Win.Coop && w.Win.Wins > 0) },
            new() { Name = "Event battles", Coop = true, Wins = events.Where(w => w.Win.Coop).Sum(w => w.Win.Wins), Battles = events.Count(w => w.Win.Coop && w.Win.Wins > 0) },
            new() { Name = "Solo areas (EXP, uncap, materia…)", Wins = areas.Where(w => !w.Win.Coop).Sum(w => w.Win.Wins), Battles = areas.Count(w => !w.Win.Coop && w.Win.Wins > 0) },
            new() { Name = "Co-op boss areas", Coop = true, Wins = areas.Where(w => w.Win.Coop).Sum(w => w.Win.Wins), Battles = areas.Count(w => w.Win.Coop && w.Win.Wins > 0) },
            new() { Name = "Criterion dungeon battles", Wins = criterion.Sum(c => Long(c, "WinCount")), Battles = criterion.Where(c => Long(c, "WinCount") > 0).Select(c => Long(c, "AnotherBattleId")).Distinct().Count() },
            new() { Name = "Highwind treasure battles", Wins = highwind, Battles = 3 },
        }.Where(x => x.Wins > 0).ToList();
        bs.SoloWins = bs.ByMode.Where(x => !x.Coop).Sum(x => x.Wins);
        bs.CoopWins = bs.ByMode.Where(x => x.Coop).Sum(x => x.Wins);
        bs.StoryBattlesCleared = List(info, "UserEpisodeBattleList").Count();

        bs.Events = events.GroupBy(w => w.EventId).Select(g => new EosEventBattles
        {
            Id = g.Key,
            Name = g.First().Win.Source,
            SoloWins = g.Where(w => !w.Win.Coop).Sum(w => w.Win.Wins),
            CoopWins = g.Where(w => w.Win.Coop).Sum(w => w.Win.Wins),
            Battles = g.Count(w => w.Win.Wins > 0),
        }).Where(e => e.TotalWins > 0).OrderByDescending(e => e.TotalWins).ToList();

        bs.Areas = areas.GroupBy(w => w.AreaKey).Select(g => new EosAreaBattles
        {
            Category = g.First().Category,
            Name = g.First().Win.Source,
            Coop = g.First().Win.Coop,
            Wins = g.Sum(w => w.Win.Wins),
            Battles = g.Count(w => w.Win.Wins > 0),
            HighScore = g.Max(w => w.Win.HighScore),
        }).Where(a => a.Wins > 0).OrderByDescending(a => a.Wins).ToList();

        bs.TopBattles = wins.Select(w => w.Win).Where(w => w.Wins > 0).OrderByDescending(w => w.Wins).Take(25).ToList();
    }

    private static void ParseCollections(JsonElement info, EosPlayerStats stats, MasterData m)
    {
        // Saved parties: members are keyed PartyId * 10 + slot.
        var members = List(info, "UserPartyMemberList")
            .GroupBy(pm => Long(pm, "PartyMemberId") / 10)
            .ToDictionary(g => g.Key, g => g.OrderBy(pm => Long(pm, "Idx")).ToList());
        foreach (var p in List(info, "UserPartyList"))
        {
            var partyId = Long(p, "PartyId");
            var party = new EosParty
            {
                Name = Str(p, "Name") is { Length: > 0 } name ? name : $"Unnamed party ({partyId})",
                CombatPower = Long(p, "CombatPower"),
            };
            foreach (var pm in members.GetValueOrDefault(partyId) ?? new())
            {
                var charId = Long(pm, "CharacterId");
                if (charId <= 0) continue;
                party.Members.Add(new EosPartyMember
                {
                    Character = CharacterName(m, charId),
                    MainWeapon = m.Weapons.TryGetValue(Long(pm, "WeaponId0"), out var w) ? w.Name : null,
                    Outfit = m.CostumeNames.GetValueOrDefault(Long(pm, "CostumeId")),
                });
            }
            // Emptied slots keep a stale combat power, so only list parties that still have members.
            if (party.Members.Count > 0) stats.Parties.Add(party);
        }
        stats.Parties = stats.Parties.OrderByDescending(p => p.CombatPower).ToList();

        // Chocobos: stat weights are out of 10,000.
        foreach (var c in List(info, "UserChocoboList"))
        {
            var weights = new[] { "SpeedWeight", "StaminaWeight", "IntelligenceWeight", "AdaptabilityWeight" }.Select(k => Long(c, k)).ToArray();
            var sum = Math.Max(1, weights.Sum());
            stats.Chocobos.Add(new EosChocobo
            {
                Rarity = m.ChocoboRarity.GetValueOrDefault(Long(c, "ChocoboId")),
                Rank = (int)Long(c, "RankType"),
                RankLimit = (int)Long(c, "LimitRankType"),
                Balance = weights.Select(v => (int)Math.Round(100.0 * v / sum)).ToArray(),
                Obtained = FromMs(Long(c, "GetDatetime")),
                Locked = c.TryGetProperty("IsLock", out var l) && l.ValueKind == JsonValueKind.True,
            });
        }
        stats.Chocobos = stats.Chocobos.OrderByDescending(c => c.Rank).ThenByDescending(c => c.Rarity).ThenBy(c => c.Obtained).ToList();
        stats.ChocoboExpeditions = List(info, "UserChocoboExpeditionDeckList")
            .Where(d => Long(d, "ChocoboExpeditionId") > 0)
            .Select(d => new EosChocoboExpedition
            {
                Area = m.ChocoboExpeditions.GetValueOrDefault(Long(d, "ChocoboExpeditionId")) ?? $"Area #{Long(d, "ChocoboExpeditionId")}",
                Character = Long(d, "CharacterId") > 0 ? CharacterName(m, Long(d, "CharacterId")) : null,
                Started = FromMs(Long(d, "StartDatetime")),
            }).ToList();

        // Materia inventory, one row per materia with its rolled sub stats.
        var statOrder = new Dictionary<string, int>();
        stats.MateriaOwned = List(info, "UserMateriaList").Select(x =>
            {
                var owned = new EosMateriaOwned
                {
                    Name = m.MateriaName(Long(x, "MateriaId"), Long(x, "NotesSetId")),
                    Stars = (int)Long(x, "QualityType"),
                    Locked = x.TryGetProperty("IsLock", out var l) && l.ValueKind == JsonValueKind.True,
                    Obtained = Long(x, "GetDatetime") > 0 ? FromMs(Long(x, "GetDatetime")) : null,
                };
                var open = (int)Long(x, "ParameterOpenCount");
                for (var i = 0; i < Math.Min(open, 4); i++)
                {
                    var id = Long(x, $"ParameterId{i}");
                    if (id <= 0) continue;
                    var value = (decimal)Long(x, $"ParameterValue{i}");
                    var def = m.MateriaStats.TryGetValue(id, out var d) ? d : (Label: $"Stat #{id}", Percent: false, Order: 999);
                    // Percent sub stats are stored in tenths of a percent.
                    if (def.Percent) value /= 10m;
                    owned.Stats[def.Label] = owned.Stats.GetValueOrDefault(def.Label) + value;
                    statOrder.TryAdd(def.Label, def.Order);
                }
                return owned;
            })
            .OrderByDescending(x => x.Stars).ThenBy(x => x.Name).ThenByDescending(x => x.Obtained)
            .ToList();
        stats.MateriaStatColumns = statOrder.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();

        // Criterion dungeons ("another dungeons" in the data). Battle rows repeat per boss enhancement level.
        var userBattles = List(info, "UserAnotherBattleList")
            .GroupBy(b => Long(b, "AnotherBattleId"))
            .ToDictionary(g => g.Key, g => g.Select(b => (Stage: (int)Long(b, "BossEnhanceStage"), Wins: Long(b, "WinCount"))).ToList());
        stats.CriterionDungeons = List(info, "UserAnotherDungeonList").Select(d =>
        {
            m.CriterionDungeons.TryGetValue(Long(d, "AnotherDungeonId"), out var def);
            var rank = (int)Long(d, "HighRankType");
            var battles = (m.CriterionBattles.GetValueOrDefault(Long(d, "AnotherDungeonId")) ?? new())
                .OrderBy(b => b.Idx)
                .Select(b =>
                {
                    var rows = userBattles.GetValueOrDefault(b.Id) ?? new();
                    var won = rows.Where(r => r.Wins > 0).ToList();
                    return new EosDungeonBattle
                    {
                        Idx = b.Idx,
                        Kind = b.Kind,
                        Enemy = string.IsNullOrEmpty(b.Enemy) ? "–" : b.Enemy,
                        MaxEnhancement = won.Count > 0 ? won.Max(r => r.Stage) : null,
                        Wins = rows.Sum(r => r.Wins),
                    };
                }).ToList();
            var team = new[] { "UsedCharacterId0", "UsedCharacterId1", "UsedCharacterId2" }
                .Select(k => Long(d, k)).Where(id => id > 0).Select(id => CharacterName(m, id)).ToList();
            return new EosDungeonStat
            {
                Name = string.IsNullOrEmpty(def.Name) ? $"Dungeon #{Long(d, "AnotherDungeonId")}" : def.Name,
                Difficulty = def.Difficulty,
                BestRank = rank > 0 && rank < DungeonRanks.Length ? DungeonRanks[rank] : "–",
                RankOrder = rank,
                HighScore = Long(d, "HighScore"),
                Clears = Long(d, "WinCount"),
                Team = team.Count > 0 ? string.Join(", ", team) : null,
                LastImproved = FromMs(Long(d, "LastUpdateScoreDatetime")),
                Battles = battles,
            };
        }).OrderByDescending(d => d.LastImproved).ToList();

        // Crash battles: cleared when the matching solo/co-op battle has at least one win.
        var soloWins = List(info, "UserEventSoloBattleList").GroupBy(b => Long(b, "EventSoloBattleId")).ToDictionary(g => g.Key, g => g.First());
        var multiWins = List(info, "UserEventMultiBattleList").GroupBy(b => Long(b, "EventMultiBattleId")).ToDictionary(g => g.Key, g => g.First());
        stats.CrashBattles = m.CrashBattles.OrderBy(c => c.Coop).ThenBy(c => c.Id).Select(c =>
        {
            var found = (c.Coop ? multiWins : soloWins).TryGetValue(c.Id, out var row);
            var name = (c.Coop ? m.MultiBattleNames : m.SoloBattleNames).GetValueOrDefault(c.Id);
            return new EosCrashBattle
            {
                Name = string.IsNullOrEmpty(name) ? $"Battle #{c.Id}" : name,
                Coop = c.Coop,
                Attempted = found,
                Wins = found ? Long(row, "TotalWinCount") : 0,
                HighScore = found ? Long(row, "HighScore") : 0,
            };
        }).ToList();

        stats.EscalationChallenges = List(info, "UserBossChallengeList").Select(b =>
        {
            var id = Long(b, "BossChallengeId");
            m.BossChallenges.TryGetValue(id, out var def);
            return new EosProgressStat
            {
                Name = string.IsNullOrEmpty(def.Name) ? $"Escalation Challenge #{id}" : def.Name,
                Cleared = (int)Long(b, "ClearLevel"),
                Total = def.MaxLevel,
            };
        }).OrderBy(b => b.Name).ToList();

        // Season passes: one bit per claimed reward on each track.
        var bought = List(info, "UserShopItemList").Select(s => Long(s, "ShopItemId")).ToHashSet();
        static int Bits(JsonElement e, string prefix, int count) =>
            Enumerable.Range(0, count).Sum(i => System.Numerics.BitOperations.PopCount((ulong)(uint)Long(e, prefix + i)));
        stats.SeasonPasses = List(info, "UserSeasonPassList").Select(sp =>
        {
            m.SeasonPasses.TryGetValue(Long(sp, "SeasonPassId"), out var def);
            var premium = Bits(sp, "ExtraReceivedStepIdxFlags", 4);
            return new EosSeasonPass
            {
                Name = string.IsNullOrEmpty(def.Name) ? $"Season Pass #{Long(sp, "SeasonPassId")}" : def.Name,
                Ended = FromMs(def.EndMs),
                Steps = def.Steps,
                FreeClaimed = Math.Min(def.Steps > 0 ? def.Steps : int.MaxValue, Bits(sp, "ReceivedStepIdxFlags", 2)),
                PremiumClaimed = premium,
                HasPremium = premium > 0 || (def.PremiumShopItemId > 0 && bought.Contains(def.PremiumShopItemId)),
            };
        }).OrderByDescending(s => s.Ended).ToList();

        // Steam achievements: the game data has no names for them, only unlock dates.
        var unlocks = List(info, "UserSteamAchievementList").Select(a => FromMs(Long(a, "AchievementDatetime"))).Where(d => d is not null).Select(d => d!.Value).ToList();
        stats.Achievements = new EosAchievements
        {
            Unlocked = List(info, "UserSteamAchievementList").Count(),
            Total = m.TableCounts.GetValueOrDefault("SteamAchievement"),
            First = unlocks.Count > 0 ? unlocks.Min() : null,
            Latest = unlocks.Count > 0 ? unlocks.Max() : null,
            ByYear = unlocks.GroupBy(d => d.Year).OrderBy(g => g.Key)
                .Select(g => new EosNamedCount { Name = g.Key.ToString(), Value = g.Count() }).ToList(),
        };
    }

    private static EosPurchases ParsePurchases(JsonElement info, MasterData m)
    {
        var result = new EosPurchases();
        var shops = new Dictionary<string, EosShopSummary>();
        foreach (var s in List(info, "UserShopItemList"))
        {
            var count = (int)Long(s, "TotalPurchaseCount");
            if (count <= 0 || !m.ShopItems.TryGetValue(Long(s, "ShopItemId"), out var item)) continue;
            var last = FromMs(Long(s, "LastPurchaseDatetime"));
            var shopName = m.ShopNames.GetValueOrDefault(item.ShopId) ?? "Shop";
            if (item.StoreGroupId > 0 && m.StoreProducts.TryGetValue(item.StoreGroupId, out var product))
            {
                result.Paid.Add(new EosPaidPurchase
                {
                    Name = !string.IsNullOrEmpty(item.Name) ? item.Name : !string.IsNullOrEmpty(product.Name) ? product.Name : shopName,
                    Shop = shopName,
                    Count = count,
                    PriceUsd = product.PriceUsd,
                    Crystals = product.Crystals,
                    LastPurchased = last,
                });
                continue;
            }
            if (!shops.TryGetValue(shopName, out var summary)) shops[shopName] = summary = new EosShopSummary { Name = shopName };
            summary.ItemsBought++;
            summary.TotalPurchases += count;
            if (last > summary.LastPurchased || summary.LastPurchased is null) summary.LastPurchased = last;
        }
        result.Paid = result.Paid.OrderByDescending(p => p.LastPurchased).ToList();
        result.Shops = shops.Values.OrderByDescending(s => s.LastPurchased).ToList();
        return result;
    }

    private static EosDrawStat GetDraw(Dictionary<long, EosDrawStat> draws, long gachaId, MasterData m)
    {
        if (draws.TryGetValue(gachaId, out var draw)) return draw;
        m.Gachas.TryGetValue(gachaId, out var g);
        var type = g?.Type ?? -1;
        draw = new EosDrawStat
        {
            Id = gachaId,
            Type = type,
            TypeLabel = GachaTypeLabels.TryGetValue(type, out var label) ? label : "Other",
            Name = g?.Name ?? $"Draw #{gachaId}",
            FeaturedWeapons = m.FeaturedWeapons.GetValueOrDefault(gachaId),
        };
        draws[gachaId] = draw;
        return draw;
    }

    internal static string CleanName(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var text = SpriteRegex.Replace(raw, match => SpriteText.TryGetValue(match.Groups[1].Value, out var t) ? t : string.Empty);
        text = TagRegex.Replace(text, string.Empty);
        return SpaceRegex.Replace(text, " ").Trim();
    }

    private static IEnumerable<JsonElement> List(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed record GachaInfo(int Type, string Name);
    private sealed record StepInfo(int Seq, int NextSeq, int ConsumptionType, long ConsumptionCount, long DrawCount);
    private sealed record WeaponInfo(string Name, string Character);
    private sealed record GrowthNode(int Idx, bool Flat, long Hp, long PAtk, long MAtk, long PDef, long MDef, long Heal);
    private sealed record HighwindInfo(int Type, int DirectionType);
    private sealed record MissionInfo(string Text, long Goal, long GroupType, string Category);
    private sealed record StoreProductInfo(string Name, decimal PriceUsd, long Crystals);

    private sealed class MasterData
    {
        public Dictionary<long, GachaInfo> Gachas { get; } = new();
        public Dictionary<long, long> StepGroupGacha { get; } = new();
        public Dictionary<long, List<StepInfo>> StepsByGroup { get; } = new();
        public Dictionary<long, string> ItemNames { get; } = new();
        public Dictionary<long, WeaponInfo> Weapons { get; } = new();
        public Dictionary<long, string> FeaturedWeapons { get; } = new();
        public Dictionary<long, string> RecipeNames { get; } = new();
        public Dictionary<long, HighwindInfo> HighwindBattles { get; } = new();

        public static MasterData Load(string basePath, ILogger logger)
        {
            var data = new MasterData();
            var master = Path.Combine(basePath, "MasterData", "gl");
            var loc = LoadLocalization(Path.Combine(basePath, "Localization", "en.json"), logger);
            string Name(JsonElement e, string field) => CleanName(loc.TryGetValue(Long(e, field), out var s) ? s : null);

            // Time windows (condition type 2) per condition set, used to tell ticket draws apart.
            var windows = new Dictionary<long, (long Start, long End)>();
            var conditionWindows = Rows(master, "Condition", logger)
                .Where(c => Long(c, "ConditionType") == 2)
                .ToDictionary(c => Long(c, "Id"), c => (Start: Long(c, "StartDatetime"), End: Long(c, "EndDatetime")));
            foreach (var rel in Rows(master, "ConditionSetRel", logger))
            {
                if (!conditionWindows.TryGetValue(Long(rel, "ConditionId"), out var w)) continue;
                var set = Long(rel, "ConditionSetId");
                // All conditions in a set must hold, so the window is their overlap.
                windows[set] = windows.TryGetValue(set, out var cur)
                    ? (Math.Max(cur.Start, w.Start), cur.End == 0 ? w.End : w.End == 0 ? cur.End : Math.Min(cur.End, w.End))
                    : w;
            }

            foreach (var g in Rows(master, "Gacha", logger))
            {
                var type = (int)Long(g, "GachaType");
                var name = Name(g, "NameLanguageId");
                // Every ticket draw is just called "Ticket Draw"; add the run dates so they can be told apart.
                if (type == 3 && windows.TryGetValue(Long(g, "DisplayConditionSetId"), out var run) && run.Start > 0)
                {
                    var from = DateTimeOffset.FromUnixTimeMilliseconds(run.Start).UtcDateTime.ToString("MMM d, yyyy");
                    name += run.End > 0
                        ? $" ({from} – {DateTimeOffset.FromUnixTimeMilliseconds(run.End).UtcDateTime:MMM d, yyyy})"
                        : $" (from {from})";
                }
                data.Gachas[Long(g, "Id")] = new GachaInfo(type, name);
            }

            foreach (var sg in Rows(master, "GachaStepGroup", logger))
                data.StepGroupGacha[Long(sg, "Id")] = Long(sg, "GachaId");

            foreach (var s in Rows(master, "GachaStep", logger))
            {
                var groupId = Long(s, "GachaStepGroupId");
                if (!data.StepsByGroup.TryGetValue(groupId, out var list)) data.StepsByGroup[groupId] = list = new();
                list.Add(new StepInfo((int)Long(s, "Seq"), (int)Long(s, "NextSeq"), (int)Long(s, "GachaConsumptionType"),
                    Long(s, "ConsumptionCount"), Long(s, "DrawCount")));
            }
            foreach (var list in data.StepsByGroup.Values) list.Sort((a, b) => a.Seq.CompareTo(b.Seq));

            foreach (var i in Rows(master, "Item", logger))
                data.ItemNames[Long(i, "Id")] = Name(i, "NameLanguageId");

            var characters = Rows(master, "Character", logger).ToDictionary(c => Long(c, "Id"), c => Name(c, "NameLanguageId"));
            foreach (var w in Rows(master, "Weapon", logger))
            {
                characters.TryGetValue(Long(w, "CharacterId"), out var ch);
                data.Weapons[Long(w, "Id")] = new WeaponInfo(Name(w, "NameLanguageId"), ch ?? string.Empty);
                if (Long(w, "WeaponMedalItemId") > 0) data.WeaponParts[Long(w, "Id")] = Long(w, "WeaponMedalItemId");
                data.WeaponGrowth[Long(w, "Id")] = (Long(w, "BaseExp"), Long(w, "WeaponLevelGroupId"), Long(w, "WeaponReleaseSettingGroupId"), Long(w, "WeaponRaritySettingGroupId"));
            }
            foreach (var l in Rows(master, "WeaponLevel", logger))
            {
                var g = Long(l, "WeaponLevelGroupId");
                if (!data.WeaponLevelExp.TryGetValue(g, out var list)) data.WeaponLevelExp[g] = list = new List<(int, long)>();
                list.Add(((int)Long(l, "Level"), Long(l, "ExpCoefficient")));
            }
            foreach (var list in data.WeaponLevelExp.Values) list.Sort((a, b) => a.Level.CompareTo(b.Level));
            foreach (var r in Rows(master, "WeaponReleaseSetting", logger))
            {
                var g = Long(r, "WeaponReleaseSettingGroupId");
                if (!data.WeaponLevelLimits.TryGetValue(g, out var caps)) data.WeaponLevelLimits[g] = caps = new Dictionary<int, int>();
                caps[(int)Long(r, "ReleaseCount")] = (int)Long(r, "LevelLimit");
            }
            foreach (var r in Rows(master, "WeaponRaritySetting", logger))
                data.WeaponMaxRelease[(Long(r, "WeaponRaritySettingGroupId"), (int)Long(r, "RarityType"))] = (int)Long(r, "MaxReleaseCount");

            // Featured weapons per draw: older banners list them in GachaAppeal (GachaAppealId0..9),
            // newer ones in GachaAppeal2Weapon, whose group id is the gacha id * 100 + n.
            var appealWeapons = Rows(master, "GachaAppeal", logger).ToDictionary(a => Long(a, "Id"), a => Long(a, "WeaponId"));
            var featured = new Dictionary<long, List<long>>();
            void AddFeatured(long gachaId, long weaponId)
            {
                if (weaponId <= 0) return;
                if (!featured.TryGetValue(gachaId, out var list)) featured[gachaId] = list = new();
                if (!list.Contains(weaponId)) list.Add(weaponId);
            }
            foreach (var g in Rows(master, "Gacha", logger))
                for (var i = 0; i < 10; i++)
                    AddFeatured(Long(g, "Id"), appealWeapons.GetValueOrDefault(Long(g, $"GachaAppealId{i}")));
            foreach (var a in Rows(master, "GachaAppeal2Weapon", logger))
                AddFeatured(Long(a, "GachaAppeal2WeaponGroupId") / 100, Long(a, "WeaponId"));
            foreach (var (gachaId, weaponIds) in featured)
                data.FeaturedWeapons[gachaId] = string.Join(", ", weaponIds
                    .Select(id => data.Weapons.TryGetValue(id, out var w) ? w.Name : null)
                    .Where(n => !string.IsNullOrEmpty(n)));

            foreach (var r in Rows(master, "MateriaRecipe", logger))
                data.RecipeNames[Long(r, "Id")] = Name(r, "TitleLanguageId");

            foreach (var h in Rows(master, "HighwindBattle", logger))
                data.HighwindBattles[Long(h, "Id")] = new HighwindInfo((int)Long(h, "HighwindBattleType"), (int)Long(h, "HighwindBattleReleaseDirectionType"));

            // --- Account / progress extras ---
            data.UserRankExp = Rows(master, "UserRank", logger).Select(r => ((int)Long(r, "Rank"), Long(r, "RequiredExp"))).OrderBy(r => r.Item1).ToList();
            data.GuildLevelExp = Rows(master, "GuildLevel", logger).Select(r => ((int)Long(r, "Level"), Long(r, "Exp"))).OrderBy(r => r.Item1).ToList();
            foreach (var cl in Rows(master, "CharacterLevel", logger))
            {
                var id = Long(cl, "CharacterId");
                if (!data.CharacterLevelExp.TryGetValue(id, out var list)) data.CharacterLevelExp[id] = list = new();
                list.Add(((int)Long(cl, "Level"), Long(cl, "Exp")));
            }
            foreach (var list in data.CharacterLevelExp.Values) list.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            foreach (var c in Rows(master, "Character", logger))
                data.Characters[Long(c, "Id")] = (Name(c, "NameLanguageId"), (int)Long(c, "OrderNo"));
            foreach (var c in Rows(master, "CharacterCostume", logger))
                data.CostumeCharacter[Long(c, "Id")] = Long(c, "CharacterId");
            // Skip story-only weapons players can't obtain (same list the weapon search uses).
            foreach (var w in Rows(master, "Weapon", logger).Where(w => !WeaponSearchDataService.ExcludedWeaponIds.Contains((int)Long(w, "Id"))))
                data.WeaponCharacter[Long(w, "Id")] = Long(w, "CharacterId");

            data.TableCounts["Memoria"] = Rows(master, "Memoria", logger).Count;
            data.TableCounts["Summon"] = Rows(master, "Summon", logger).Count;
            data.TableCounts["HomeBackground"] = Rows(master, "HomeBackground", logger).Count;
            data.TableCounts["Honor"] = Rows(master, "Honor", logger).Count;

            foreach (var g in Rows(master, "ProfilePlayRecordGroup", logger))
                data.RecordGroups[Long(g, "Id")] = (Name(g, "NameLanguageId"), (int)Long(g, "OrderNo"));
            foreach (var p in Rows(master, "ProfilePlayRecord", logger))
                data.ProfileRecords[Long(p, "PlayRecordType")] = (Name(p, "NameLanguageId"), (int)Long(p, "OrderNo"), Long(p, "ProfilePlayRecordGroupId"));

            foreach (var a in Rows(master, "Award", logger))
            {
                var name = Name(a, "NameLanguageId");
                // "Place within the top 20 in the {0} {1} time(s)." -> "Place within the top 20 in the Battle Ranking."
                var desc = (loc.TryGetValue(Long(a, "DescriptionLanguageId"), out var raw) ? raw : string.Empty)
                    .Replace(" {1} time(s)", string.Empty).Replace("{0}", name);
                data.Awards[Long(a, "Id")] = (name, CleanName(desc), (int)Long(a, "OrderNo"));
            }
            foreach (var r in Rows(master, "AwardRanking", logger))
            {
                var id = Long(r, "AwardId");
                if (!data.AwardRankings.TryGetValue(id, out var list)) data.AwardRankings[id] = list = new();
                list.Add((Long(r, "EventBaseId"), (int)Long(r, "Rank")));
            }

            var eventNames = Rows(master, "EventBase", logger).ToDictionary(e => Long(e, "Id"), e => Name(e, "NameLanguageId"));
            foreach (var e in Rows(master, "EventCrisisBattle", logger))
                data.CrisisEvents[Long(e, "Id")] = (eventNames.GetValueOrDefault(Long(e, "EventBaseId")) ?? string.Empty, Long(e, "EventBaseId"), Long(e, "FixRankingDatetime"));
            foreach (var e in Rows(master, "EventDamageRankingBattle", logger))
                data.DamageRankingEvents.Add(Long(e, "EventCrisisBattleId"));
            foreach (var e in Rows(master, "EventGuildRanking", logger))
                data.GuildBattles[Long(e, "EventBaseId")] = (eventNames.GetValueOrDefault(Long(e, "EventBaseId")) ?? string.Empty, Long(e, "MainBattleEndDatetime"));
            foreach (var e in Rows(master, "EventScoreDungeon", logger))
                data.ScoreDungeons[Long(e, "Id")] = (eventNames.GetValueOrDefault(Long(e, "EventBaseId")) ?? string.Empty, Long(e, "EventBaseId"), Long(e, "FixRankingDatetime"));

            // FinalFloorSeq says 100 for the 50-floor Colosseum towers, so count the actual floors.
            var towerFloors = Rows(master, "TowerFloor", logger).GroupBy(f => Long(f, "TowerId")).ToDictionary(g => g.Key, g => g.Count());
            foreach (var t in Rows(master, "Tower", logger))
                data.Towers[Long(t, "Id")] = (Name(t, "NameLanguageId"), towerFloors.GetValueOrDefault(Long(t, "Id"), (int)Long(t, "FinalFloorSeq")));
            // Events filed under the "Battle Tower: Singularity" category (localization 868000000000010).
            var singularity = Rows(master, "EventBase", logger).Where(e => Long(e, "EventTopCategoryLanguageId") == 868000000000010)
                .ToDictionary(e => Long(e, "Id"), e => Name(e, "EventTopTitleLanguageId").Replace("\\n", " ").Replace("\n", " ").Replace("  ", " ").Trim());
            var singularityFloors = Rows(master, "EventSoloBattle", logger).Select(b => Long(b, "Id") / 1000).Where(singularity.ContainsKey)
                .GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
            foreach (var (id, name) in singularity)
                data.SingularityTowers[id] = (name, singularityFloors.GetValueOrDefault(id));
            foreach (var d in Rows(master, "DamageChallengeBattle", logger))
                data.DamageChallenges[Long(d, "Id")] = (Name(d, "NameLanguageId"), (int)Long(d, "OrderNo"));

            // --- Collections, challenges, passes and shop ---
            foreach (var c in Rows(master, "CharacterCostume", logger))
                data.CostumeNames[Long(c, "Id")] = Name(c, "NameLanguageId");
            foreach (var c in Rows(master, "Chocobo", logger))
                data.ChocoboRarity[Long(c, "Id")] = (int)Long(c, "ChocoboRarityType");
            foreach (var e in Rows(master, "ChocoboExpedition", logger))
                data.ChocoboExpeditions[Long(e, "Id")] = Name(e, "NameLanguageId");

            // Materia names come from recipe titles, e.g. "Ruin (⬤Circle) Recipe".
            var shapes = new Dictionary<long, string> { [1] = "⬤Circle", [2] = "▲Triangle", [3] = "✖Cross" };
            foreach (var r in Rows(master, "MateriaRecipe", logger))
            {
                var title = Name(r, "TitleLanguageId");
                title = title.Replace(" Recipe", string.Empty);
                var notes = Long(r, "NotesSetId");
                if (notes > 0 && shapes.TryGetValue(notes, out var shape)) title = title.Replace($" ({shape})", string.Empty);
                data.MateriaBaseNames.TryAdd(Long(r, "MateriaId"), title);
            }
            data.MateriaShapes = shapes;

            // Materia sub stats. Type 1 = base stats (detail 1-6 = HP, PATK, MATK, PDEF, MDEF, HEAL);
            // type 2 = elemental potency (parameter 2/3 = phys./mag., detail 2-7 = Fire..Wind).
            // Grant value type 2 = percent.
            var baseStats = new[] { "HP", "PATK", "MATK", "PDEF", "MDEF", "HEAL" };
            var elements = new[] { "Fire", "Ice", "Lightning", "Earth", "Water", "Wind" };
            foreach (var p in Rows(master, "MateriaParameter", logger))
            {
                var percent = Long(p, "GrantValueType") == 2;
                var detail = (int)Long(p, "MateriaParameterDetailType");
                var paramType = (int)Long(p, "ParameterType");
                string? label = null; int order = 0;
                if (Long(p, "MateriaParameterType") == 1 && detail is >= 1 and <= 6)
                {
                    label = baseStats[detail - 1] + (percent ? " %" : string.Empty);
                    order = detail * 2 + (percent ? 1 : 0);
                }
                else if (Long(p, "MateriaParameterType") == 2 && detail is >= 2 and <= 7 && paramType is 2 or 3)
                {
                    label = $"{(paramType == 2 ? "Phys." : "Mag.")} {elements[detail - 2]} Pot. %";
                    order = 100 + (detail - 2) * 2 + (paramType - 2);
                    percent = true;
                }
                if (label != null) data.MateriaStats[Long(p, "Id")] = (label, percent, order);
            }

            var areas = Rows(master, "AnotherArea", logger).ToDictionary(a => Long(a, "Id"), a => Name(a, "NameLanguageId"));
            foreach (var d in Rows(master, "AnotherDungeon", logger))
                data.CriterionDungeons[Long(d, "Id")] = (areas.GetValueOrDefault(Long(d, "AnotherAreaId")) ?? string.Empty, (int)Long(d, "DifficultyLevel"));

            // Escalation Challenges: names are EventBase-style ids 851000000000000 + id; ids 14014x02 are
            // stages of the Extreme Escalation Challenge (14014).
            var levels = Rows(master, "BossChallengeLevel", logger).GroupBy(l => Long(l, "BossChallengeId"))
                .ToDictionary(g => g.Key, g => g.Max(l => (int)Long(l, "Level")));
            foreach (var b in Rows(master, "BossChallenge", logger))
            {
                var id = Long(b, "Id");
                string name;
                if (id > 1_000_000)
                {
                    var parent = id / 1000;
                    var stage = id % 1000 / 100 + 1;
                    name = $"{CleanName(loc.GetValueOrDefault(851000000000000 + parent))} (stage {stage})";
                }
                else name = CleanName(loc.GetValueOrDefault(851000000000000 + id));
                data.BossChallenges[id] = (name.Trim(), levels.GetValueOrDefault(id));
            }

            var stepCounts = Rows(master, "SeasonPassStep", logger)
                .Where(s => Long(s, "SeasonPassRewardType") == 1)
                .GroupBy(s => Long(s, "SeasonPassStepGroupId")).ToDictionary(g => g.Key, g => g.Count());
            foreach (var s in Rows(master, "SeasonPass", logger))
                data.SeasonPasses[Long(s, "Id")] = (Name(s, "TitleLanguageId"), Long(s, "ExpiredSendGiftStartDatetime"),
                    stepCounts.GetValueOrDefault(Long(s, "SeasonPassStepGroupId")), Long(s, "ExtraShopItemId"));
            data.TableCounts["SteamAchievement"] = Rows(master, "SteamAchievement", logger).Count;

            // Shop names are often just the currency ("Co-op Medals"), so prefix the shop group ("Medal Exchange").
            var shopGroups = Rows(master, "ShopGroup", logger).ToDictionary(g => Long(g, "Id"), g => Name(g, "NameLanguageId"));
            foreach (var s in Rows(master, "Shop", logger))
            {
                var shopName = Name(s, "NameLanguageId");
                var groupName = shopGroups.GetValueOrDefault(Long(s, "ShopGroupId"));
                data.ShopNames[Long(s, "Id")] = string.IsNullOrEmpty(groupName) || groupName == shopName ? shopName
                    : string.IsNullOrEmpty(shopName) ? groupName : $"{groupName} · {shopName}";
            }
            foreach (var s in Rows(master, "ShopItem", logger))
                data.ShopItems[Long(s, "Id")] = (Name(s, "NameLanguageId"), Long(s, "ShopId"), Long(s, "StoreProductGroupId"));
            foreach (var p in Rows(master, "StoreProduct", logger))
            {
                var group = Long(p, "StoreProductGroupId");
                var english = Str(p, "AutoProductDescriptionEn");
                if (data.StoreProducts.TryGetValue(group, out var existing))
                {
                    // One row per store; keep the first price and pick up the English name when a row has it.
                    if (string.IsNullOrEmpty(existing.Name) && !string.IsNullOrEmpty(english))
                        data.StoreProducts[group] = existing with { Name = english };
                    continue;
                }
                decimal.TryParse(Str(p, "PriceUsd"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var usd);
                data.StoreProducts[group] = new StoreProductInfo(english ?? string.Empty, usd, Long(p, "PaidStoneCount") + Long(p, "FreeStoneCount"));
            }

            // Weapon voucher shops: shop items paid with a "Weapon Voucher" item that reward a weapon (Reward type 5).
            var voucherItems = Rows(master, "Item", logger)
                .Where(i => { var n = Name(i, "NameLanguageId"); return n.Contains("Weapon Voucher") && !n.Contains("Parts"); })
                .Select(i => Long(i, "Id")).ToHashSet();
            var rewards = Rows(master, "Reward", logger).ToDictionary(r => Long(r, "Id"), r => (Type: Long(r, "RewardType"), Target: Long(r, "TargetId")));
            var voucherSets = Rows(master, "ConsumptionSetConsumptionRel", logger)
                .Where(c => rewards.TryGetValue(Long(c, "RewardId"), out var rw) && rw.Type == 1 && voucherItems.Contains(rw.Target))
                .Select(c => Long(c, "ConsumptionSetId")).ToHashSet();
            var setWeapons = Rows(master, "RewardSetRewardRel", logger)
                .Where(r => rewards.TryGetValue(Long(r, "RewardId"), out var rw) && rw.Type == 5)
                .GroupBy(r => Long(r, "RewardSetId"))
                .ToDictionary(g => g.Key, g => g.Select(r => rewards[Long(r, "RewardId")].Target).ToList());
            foreach (var si in Rows(master, "ShopItem", logger))
                if (voucherSets.Contains(Long(si, "ConsumptionSetId")) && setWeapons.TryGetValue(Long(si, "RewardSetId"), out var vw))
                    data.VoucherShopItems[Long(si, "Id")] = vw;

            // Growth boards (1 = character, 5 = character overaccel, 4 = summon, 6 = enemy ability).
            foreach (var g in Rows(master, "GrowthBoardGroup", logger))
                data.GrowthGroups[Long(g, "Id")] = ((int)Long(g, "GrowthBoardType"), Long(g, "TargetId"));
            foreach (var n in Rows(master, "GrowthBoardNode", logger))
            {
                var gid = Long(n, "GrowthBoardGroupId");
                if (!data.GrowthNodes.TryGetValue(gid, out var list)) data.GrowthNodes[gid] = list = new List<GrowthNode>();
                list.Add(new GrowthNode((int)Long(n, "GrowthBoardGroupIdx"), Long(n, "GrantValueType") == 1,
                    Long(n, "HpNodeStatusValue"), Long(n, "PhysicalAttackNodeStatusValue"), Long(n, "MagicalAttackNodeStatusValue"),
                    Long(n, "PhysicalDefenseNodeStatusValue"), Long(n, "MagicalDefenseNodeStatusValue"), Long(n, "HealingPowerNodeStatusValue")));
            }
            foreach (var x in Rows(master, "Summon", logger)) data.SummonNames[Long(x, "Id")] = Name(x, "NameLanguageId");
            foreach (var x in Rows(master, "AbilityEnemy", logger)) data.AbilityEnemyNames[Long(x, "Id")] = Name(x, "NameLanguageId");

            // Wishlists: GachaWishChoice rows belong to a GachaWish, which draws reference via Gacha.GachaWishId.
            foreach (var c in Rows(master, "GachaWishChoice", logger))
                data.WishChoices[Long(c, "Id")] = (Long(c, "GachaWishId"), (int)Long(c, "OrderNo"), Long(c, "DefaultWeaponId"));
            foreach (var g in Rows(master, "Gacha", logger))
            {
                var wish = Long(g, "GachaWishId");
                if (wish <= 0) continue;
                if (!data.WishGachas.TryGetValue(wish, out var gl)) data.WishGachas[wish] = gl = new List<long>();
                gl.Add(Long(g, "Id"));
            }

            foreach (var lb in Rows(master, "LoginBonus", logger)) data.LoginBonusTypes[Long(lb, "Id")] = (int)Long(lb, "LoginBonusType");
            // LimitedReleaseTargetType 1 = a shop item.
            foreach (var lr in Rows(master, "LimitedRelease", logger))
                if (Long(lr, "LimitedReleaseTargetType") == 1)
                {
                    data.LimitedReleaseShopItem[Long(lr, "Id")] = Long(lr, "TargetId");
                    data.LimitedReleaseNames[Long(lr, "Id")] = Name(lr, "NameLanguageId");
                }
            // Red-crystal price of shop items (Reward type 10 = crystal, target 1 = red).
            var redCrystalCost = Rows(master, "ConsumptionSetConsumptionRel", logger)
                .Where(c => rewards.TryGetValue(Long(c, "RewardId"), out var rw) && rw.Type == 10 && rw.Target == 1)
                .GroupBy(c => Long(c, "ConsumptionSetId")).ToDictionary(g => g.Key, g => g.Sum(c => Long(c, "ConsumptionCount")));
            foreach (var si in Rows(master, "ShopItem", logger))
                if (redCrystalCost.TryGetValue(Long(si, "ConsumptionSetId"), out var price)) data.ShopItemCrystalPrice[Long(si, "Id")] = price;

            // Event box draws belong to an event through EventBase.BoxGachaGroupId.
            foreach (var b in Rows(master, "BoxGacha", logger))
                data.BoxGachas[Long(b, "Id")] = (Name(b, "NameLanguageId"), Long(b, "BoxGachaGroupId"), Long(b, "ConsumptionItemId"), Long(b, "ConsumptionItemCount"));
            foreach (var e in Rows(master, "EventBase", logger))
                if (Long(e, "BoxGachaGroupId") > 0) data.BoxGroupEvent[Long(e, "BoxGachaGroupId")] = Long(e, "Id");

            // Battle names for solo/co-op event battles (Crash battles, Crisis and Damage Ranking stages).
            foreach (var b in Rows(master, "EventSoloBattle", logger)) data.SoloBattleNames[Long(b, "Id")] = Name(b, "NameLanguageId");
            foreach (var b in Rows(master, "EventMultiBattle", logger))
            {
                data.MultiBattleNames[Long(b, "Id")] = Name(b, "NameLanguageId");
                data.MultiBattleEvent[Long(b, "Id")] = Long(b, "EventBaseId");
            }
            // Event solo battles belong to an event through their area and area group.
            var soloAreaEvent = Rows(master, "EventSoloAreaGroup", logger).ToDictionary(g => Long(g, "Id"), g => Long(g, "EventBaseId"));
            var soloAreaGroup = Rows(master, "EventSoloArea", logger).ToDictionary(a => Long(a, "Id"), a => Long(a, "EventSoloAreaGroupId"));
            foreach (var b in Rows(master, "EventSoloBattle", logger))
                if (soloAreaGroup.TryGetValue(Long(b, "EventSoloAreaId"), out var sg) && soloAreaEvent.TryGetValue(sg, out var se))
                    data.SoloBattleEvent[Long(b, "Id")] = se;
            foreach (var e in Rows(master, "EventBase", logger)) data.EventNames[Long(e, "Id")] = Name(e, "NameLanguageId");

            // Regular solo areas (EXP, uncap, materia...) and co-op boss areas.
            var soloGroups = Rows(master, "SoloAreaGroup", logger).ToDictionary(g => Long(g, "Id"), g => Name(g, "NameLanguageId"));
            var soloAreas = Rows(master, "SoloArea", logger).ToDictionary(a => Long(a, "Id"), a => (Name: Name(a, "NameLanguageId"), Group: soloGroups.GetValueOrDefault(Long(a, "SoloAreaGroupId")) ?? string.Empty));
            foreach (var b in Rows(master, "SoloAreaBattle", logger))
            {
                var area = soloAreas.GetValueOrDefault(Long(b, "SoloAreaId"));
                data.SoloAreaBattles[Long(b, "Id")] = (area.Name ?? string.Empty, area.Group ?? string.Empty, Name(b, "NameLanguageId"));
            }
            var multiAreas = Rows(master, "MultiArea", logger).ToDictionary(a => Long(a, "Id"), a => (Name: Name(a, "NameLanguageId"), Label: Name(a, "LabelLanguageId")));
            foreach (var b in Rows(master, "MultiAreaBattle", logger))
            {
                var area = multiAreas.GetValueOrDefault(Long(b, "MultiAreaId"));
                data.MultiAreaBattles[Long(b, "Id")] = (area.Name ?? string.Empty, area.Label ?? string.Empty, Name(b, "NameLanguageId"));
            }
            // Crash battles are the ones that count toward the Crash badges (AwardBattle 10000 solo, 20000 co-op).
            foreach (var a in Rows(master, "AwardBattle", logger))
            {
                var award = Long(a, "AwardId");
                if (award == 10000 || award == 20000) data.CrashBattles.Add((Long(a, "EventBattleId"), award == 20000));
            }

            // Enemy names for a battle: Battle -> BattleWave -> BattleEnemy -> Enemy, using the targets of the last wave.
            var battleWaves = Rows(master, "Battle", logger).ToDictionary(b => Long(b, "Id"), b => Long(b, "WaveGroupId"));
            var waves = Rows(master, "BattleWave", logger).GroupBy(w => Long(w, "WaveGroupId"))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(w => Long(w, "Idx")).Select(w => Long(w, "EnemyGroupId")).ToList());
            var groupEnemies = Rows(master, "BattleEnemy", logger).GroupBy(e => Long(e, "EnemyGroupId"))
                .ToDictionary(g => g.Key, g => g.OrderBy(e => Long(e, "Idx")).Select(e => (Id: Long(e, "EnemyId"), Target: e.TryGetProperty("IsTarget", out var t) && t.ValueKind == JsonValueKind.True)).ToList());
            var enemyNames = Rows(master, "Enemy", logger).ToDictionary(e => Long(e, "Id"), e => Name(e, "NameLanguageId"));
            string Enemies(long battleId)
            {
                if (!battleWaves.TryGetValue(battleId, out var wg) || !waves.TryGetValue(wg, out var groups)) return string.Empty;
                foreach (var g in groups)
                {
                    if (!groupEnemies.TryGetValue(g, out var list)) continue;
                    var picked = list.Any(e => e.Target) ? list.Where(e => e.Target) : list;
                    var names = picked.Select(e => enemyNames.GetValueOrDefault(e.Id) ?? string.Empty).Where(n => n.Length > 0).Distinct().ToList();
                    if (names.Count > 0) return string.Join(" & ", names);
                }
                return string.Empty;
            }

            // Criterion dungeon battles, with the enemies of the unenhanced version.
            var battleRels = Rows(master, "AnotherBattleRel", logger).GroupBy(r => Long(r, "AnotherBattleId"))
                .ToDictionary(g => g.Key, g => Long(g.OrderBy(r => Long(r, "BossEnhanceStage")).First(), "BattleId"));
            foreach (var b in Rows(master, "AnotherBattle", logger))
            {
                var dungeon = Long(b, "AnotherDungeonId");
                if (!data.CriterionBattles.TryGetValue(dungeon, out var list)) data.CriterionBattles[dungeon] = list = new();
                var chaser = b.TryGetProperty("IsChaser", out var ch) && ch.ValueKind == JsonValueKind.True;
                var kind = chaser ? "Chaser" : Long(b, "BattleType") switch { 1 => "Battle", 2 => "Boss", 3 => "Final boss", _ => "Special" };
                list.Add((Long(b, "Id"), (int)Long(b, "Idx"), kind, Enemies(battleRels.GetValueOrDefault(Long(b, "Id")))));
            }

            // Crisis stage levels: default level + StageLevelIncreaseValue of each selected modifier (matches HighStageLevel).
            foreach (var c in Rows(master, "EventCrisisBattleSoloBattle", logger)) data.CrisisStageDefaults[Long(c, "EventSoloBattleId")] = (int)Long(c, "DefaultStageLevel");
            foreach (var b in Rows(master, "EventCrisisBattleBuffDebuff", logger)) data.CrisisModifierLevels[Long(b, "Id")] = (int)Long(b, "StageLevelIncreaseValue");

            // Highwind parts (names are localization 243004+) and key items with their max upgrade.
            foreach (var p in Rows(master, "HighwindParts", logger))
                data.HighwindParts[Long(p, "Id")] = CleanName(loc.GetValueOrDefault(243003 + Long(p, "HighwindPartsType")));
            var keyUpgrades = Rows(master, "HighwindKeyItemRankUpgrade", logger).GroupBy(u => Long(u, "HighwindKeyItemRankUpgradeGroupId"))
                .ToDictionary(g => g.Key, g => g.Max(u => (int)Long(u, "UpgradeCount")));
            var keyItemNames = new Dictionary<long, string>();
            foreach (var k in Rows(master, "HighwindKeyItem", logger))
            {
                var name = Name(k, "LanguageId");
                data.HighwindKeyItems[Long(k, "Id")] = (name, keyUpgrades.GetValueOrDefault(Long(k, "HighwindKeyItemRankUpgradeGroupId")));
                keyItemNames[Long(k, "UpgradeMissionGroupId")] = name;
            }

            // Guild bonuses and achievements. Achievement text has {0} = target and {1} = times; the last progress row is the goal.
            var bonusMax = Rows(master, "GuildBonusLevel", logger).GroupBy(l => Long(l, "GuildBonusLevelGroupId")).ToDictionary(g => g.Key, g => g.Max(l => (int)Long(l, "Level")));
            foreach (var b in Rows(master, "GuildBonus", logger))
                data.GuildBonuses[Long(b, "Id")] = ($"{Name(b, "NameLanguageId")} – {Name(b, "TitleLanguageId")}", bonusMax.GetValueOrDefault(Long(b, "GuildBonusLevelGroupId")), (int)Long(b, "OrderNo"));
            var achievementSteps = Rows(master, "GuildAchievementProgress", logger).GroupBy(p => Long(p, "GuildAchievementId"))
                .ToDictionary(g => g.Key, g => g.OrderBy(p => Long(p, "ProgressCount")).Last());
            foreach (var a in Rows(master, "GuildAchievement", logger))
            {
                if (!achievementSteps.TryGetValue(Long(a, "Id"), out var step)) continue;
                var goal = Long(step, "ProgressCount");
                var target = Long(a, "TargetValue1");
                var text = CleanName(loc.GetValueOrDefault(Long(step, "DescriptionLanguageId")));
                text = target > 0 ? text.Replace("{0}", target.ToString("N0")).Replace("{1}", goal.ToString("N0")) : text.Replace("{0}", goal.ToString("N0"));
                data.GuildAchievements[Long(a, "Id")] = (text, goal, (int)Long(a, "OrderNo"));
            }

            // Memoria
            foreach (var mm in Rows(master, "Memoria", logger))
            {
                var source = Name(mm, "SourceInformationLanguageId");
                data.Memoria[Long(mm, "Id")] = (Name(mm, "NameLanguageId"), (int)Long(mm, "RarityType"), string.IsNullOrEmpty(source) ? null : source, (int)Long(mm, "RequiredFragmentCount"));
                data.MemoriaLevelGroup[Long(mm, "Id")] = Long(mm, "MemoriaParameterAnalysisPointGroupId");
            }
            // Memoria level thresholds: the level is the highest AnalysisLevel whose threshold the player's AnalysisPoint has reached.
            foreach (var g in Rows(master, "MemoriaParameterAnalysisPoint", logger).GroupBy(r => Long(r, "MemoriaParameterAnalysisPointGroupId")))
                data.MemoriaLevels[g.Key] = g.Select(r => ((int)Long(r, "AnalysisLevel"), Long(r, "ThresholdAnalysisPoint"))).OrderBy(x => x.Item1).ToList();

            // Special skills: type 1 = limit break (ContentId = character), 2 = summon skill (ContentId = summon).
            var summonNames = Rows(master, "Summon", logger).ToDictionary(x => Long(x, "Id"), x => Name(x, "NameLanguageId"));
            var skillNames = Rows(master, "SkillBase", logger).ToDictionary(x => Long(x, "Id"), x => Name(x, "NameLanguageId"));
            foreach (var sp in Rows(master, "SkillSpecial", logger))
            {
                var type = Long(sp, "SkillSpecialType");
                var content = Long(sp, "ContentId");
                // Limit breaks tied to non-playable characters (e.g. ContentId 100) are not obtainable.
                if (type == 1 && !characters.ContainsKey(content)) continue;
                var forName = type == 1 ? characters.GetValueOrDefault(content) ?? string.Empty
                    : type == 2 ? summonNames.GetValueOrDefault(content) ?? string.Empty : string.Empty;
                data.SpecialSkills[Long(sp, "Id")] = (skillNames.GetValueOrDefault(Long(sp, "SkillBaseId")) ?? $"Skill #{Long(sp, "Id")}",
                    type switch { 1 => "Limit Break", 2 => "Summon", _ => "Other" }, forName);
            }
            foreach (var o in Rows(master, "SkillOveraccel", logger))
                data.OveraccelSkills[Long(o, "Id")] = (Name(o, "NameLanguageId"), characters.GetValueOrDefault(Long(o, "CharacterId")) ?? string.Empty);

            // Missions: text template ({0} = goal), goal = last progress step, category from the mission group.
            var eventNames2 = Rows(master, "EventBase", logger).ToDictionary(e => Long(e, "Id"), e => Name(e, "NameLanguageId"));
            var groups = Rows(master, "MissionGroup", logger).ToDictionary(g => Long(g, "Id"), g =>
            {
                var id = Long(g, "Id");
                var type = Long(g, "MissionGroupType");
                var name = Name(g, "NameLanguageId");
                if (type == 9)
                {
                    // Event mission groups are 12 + event id (5 digits) [+ 2-digit index].
                    var digits = id.ToString();
                    if (digits.StartsWith("12") && digits.Length >= 7 && long.TryParse(digits.Substring(2, 5), out var eventId)
                        && eventNames2.TryGetValue(eventId, out var eventName) && eventName.Length > 0)
                        name = $"Event: {eventName}";
                }
                else if (type == 24) name = keyItemNames.TryGetValue(id, out var key) ? $"Highwind: {key}" : "Highwind";
                else if (type == 7) name = $"Limit Break: {name}";
                else if (type == 8) name = $"Summon: {name}";
                else if (type == 27) name = $"Overaccel: {name}";
                return (Type: type, Name: string.IsNullOrEmpty(name) || name.Any(c => c > 0x2E7F) ? "Other" : name);
            });
            var setGroup = Rows(master, "MissionSet", logger).ToDictionary(x => Long(x, "Id"), x => Long(x, "MissionGroupId"));
            var goals = Rows(master, "MissionProgress", logger).GroupBy(p => Long(p, "MissionId")).ToDictionary(g => g.Key, g => g.Max(p => Long(p, "ProgressCount")));
            foreach (var mi in Rows(master, "Mission", logger))
            {
                var group = groups.GetValueOrDefault(setGroup.GetValueOrDefault(Long(mi, "MissionSetId")));
                data.Missions[Long(mi, "Id")] = new MissionInfo(Name(mi, "NameTemplateLanguageId"), goals.GetValueOrDefault(Long(mi, "Id")), group.Type, group.Name ?? "Other");
            }

            // Guild battle bosses: EnemyLevel is the star count shown in game.
            foreach (var g in Rows(master, "EventGuildRankingBattle", logger))
                data.GuildFights[Long(g, "Id")] = (Long(g, "EventBaseId"), (int)Long(g, "EnemyLevel"), Enemies(Long(g, "BattleId")));

            return data;
        }

        public Dictionary<long, string> SoloBattleNames { get; } = new();
        public Dictionary<long, List<long>> VoucherShopItems { get; } = new();
        public Dictionary<long, (long BaseExp, long LevelGroup, long ReleaseGroup, long RarityGroup)> WeaponGrowth { get; } = new();
        public Dictionary<long, long> WeaponParts { get; } = new();
        public Dictionary<long, List<(int Level, long Coefficient)>> WeaponLevelExp { get; } = new();
        public Dictionary<long, Dictionary<int, int>> WeaponLevelLimits { get; } = new();
        public Dictionary<(long Group, int Rarity), int> WeaponMaxRelease { get; } = new();
        public Dictionary<long, (int Type, long Target)> GrowthGroups { get; } = new();
        public Dictionary<long, List<GrowthNode>> GrowthNodes { get; } = new();
        public Dictionary<long, string> SummonNames { get; } = new();
        public Dictionary<long, string> AbilityEnemyNames { get; } = new();
        public Dictionary<long, (long WishId, int Order, long DefaultWeapon)> WishChoices { get; } = new();
        public Dictionary<long, List<long>> WishGachas { get; } = new();
        public Dictionary<long, (string Name, long GroupId, long CostItem, long CostCount)> BoxGachas { get; } = new();
        public Dictionary<long, long> BoxGroupEvent { get; } = new();
        public Dictionary<long, int> LoginBonusTypes { get; } = new();
        public Dictionary<long, long> LimitedReleaseShopItem { get; } = new();
        public Dictionary<long, string> LimitedReleaseNames { get; } = new();
        public Dictionary<long, long> ShopItemCrystalPrice { get; } = new();
        public Dictionary<long, string> MultiBattleNames { get; } = new();
        public Dictionary<long, long> SoloBattleEvent { get; } = new();
        public Dictionary<long, long> MultiBattleEvent { get; } = new();
        public Dictionary<long, string> EventNames { get; } = new();
        public Dictionary<long, (string Area, string Group, string Battle)> SoloAreaBattles { get; } = new();
        public Dictionary<long, (string Area, string Label, string Battle)> MultiAreaBattles { get; } = new();
        public List<(long Id, bool Coop)> CrashBattles { get; } = new();
        public Dictionary<long, int> CrisisStageDefaults { get; } = new();
        public Dictionary<long, string> HighwindParts { get; } = new();
        public Dictionary<long, (string Name, int MaxUpgrades)> HighwindKeyItems { get; } = new();
        public Dictionary<long, (string? Name, int MaxLevel, int Order)> GuildBonuses { get; } = new();
        public Dictionary<long, (string? Text, long Goal, int Order)> GuildAchievements { get; } = new();
        public SortedDictionary<long, (string Name, int Rarity, string? Source, int Fragments)> Memoria { get; } = new();
        public Dictionary<long, long> MemoriaLevelGroup { get; } = new();
        public Dictionary<long, List<(int Level, long Threshold)>> MemoriaLevels { get; } = new();
        public SortedDictionary<long, (string Name, string Type, string For)> SpecialSkills { get; } = new();
        public SortedDictionary<long, (string Name, string For)> OveraccelSkills { get; } = new();
        public Dictionary<long, MissionInfo> Missions { get; } = new();
        public Dictionary<long, int> CrisisModifierLevels { get; } = new();
        public Dictionary<long, List<(long Id, int Idx, string Kind, string Enemy)>> CriterionBattles { get; } = new();
        public Dictionary<long, (long EventBaseId, int Stars, string Boss)> GuildFights { get; } = new();

        public List<(int Rank, long Exp)> UserRankExp { get; set; } = new();
        public List<(int Level, long Exp)> GuildLevelExp { get; set; } = new();
        public Dictionary<long, List<(int Level, long Exp)>> CharacterLevelExp { get; } = new();
        public Dictionary<long, (string Name, int Order)> Characters { get; } = new();
        public Dictionary<long, long> CostumeCharacter { get; } = new();
        public Dictionary<long, long> WeaponCharacter { get; } = new();
        public Dictionary<string, int> TableCounts { get; } = new();
        public Dictionary<long, (string Name, int Order)> RecordGroups { get; } = new();
        public Dictionary<long, (string Name, int Order, long GroupId)> ProfileRecords { get; } = new();
        public Dictionary<long, (string Name, string Description, int Order)> Awards { get; } = new();
        public Dictionary<long, List<(long EventBaseId, int Rank)>> AwardRankings { get; } = new();
        public Dictionary<long, (string Name, long EventBaseId, long EndMs)> CrisisEvents { get; } = new();
        public HashSet<long> DamageRankingEvents { get; } = new();
        public Dictionary<long, (string Name, long EndMs)> GuildBattles { get; } = new();
        public Dictionary<long, (string Name, long EventBaseId, long EndMs)> ScoreDungeons { get; } = new();
        public Dictionary<long, (string Name, int Floors)> Towers { get; } = new();
        public Dictionary<long, (string Name, int Floors)> SingularityTowers { get; } = new();
        public Dictionary<long, (string Name, int Order)> DamageChallenges { get; } = new();
        public Dictionary<long, string> CostumeNames { get; } = new();
        public Dictionary<long, int> ChocoboRarity { get; } = new();
        public Dictionary<long, string> ChocoboExpeditions { get; } = new();
        public Dictionary<long, string> MateriaBaseNames { get; } = new();
        public Dictionary<long, string> MateriaShapes { get; set; } = new();
        public Dictionary<long, (string Label, bool Percent, int Order)> MateriaStats { get; } = new();
        public Dictionary<long, (string Name, int Difficulty)> CriterionDungeons { get; } = new();
        public Dictionary<long, (string Name, int MaxLevel)> BossChallenges { get; } = new();
        public Dictionary<long, (string Name, long EndMs, int Steps, long PremiumShopItemId)> SeasonPasses { get; } = new();
        public Dictionary<long, string> ShopNames { get; } = new();
        public Dictionary<long, (string Name, long ShopId, long StoreGroupId)> ShopItems { get; } = new();
        public Dictionary<long, StoreProductInfo> StoreProducts { get; } = new();

        public string MateriaName(long materiaId, long notesSetId)
        {
            var name = MateriaBaseNames.TryGetValue(materiaId, out var n) && n.Length > 0 ? n : $"Materia #{materiaId}";
            return MateriaShapes.TryGetValue(notesSetId, out var shape) ? $"{name} ({shape})" : name;
        }

        /// <summary>Highest level whose required exp is met, from an ascending (level, exp) table.</summary>
        public static int LevelFor(List<(int Level, long Exp)> table, long exp) =>
            table.LastOrDefault(r => r.Exp <= exp).Level;

        private static List<JsonElement> Rows(string dir, string table, ILogger logger)
        {
            var path = Path.Combine(dir, table + ".json");
            if (!File.Exists(path))
            {
                logger.LogWarning("EOS stats: master data table missing at {Path}", path);
                return new();
            }
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList()
                : new();
        }

        private static Dictionary<long, string> LoadLocalization(string path, ILogger logger)
        {
            var map = new Dictionary<long, string>();
            if (!File.Exists(path))
            {
                logger.LogWarning("EOS stats: localization file missing at {Path}", path);
                return map;
            }
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            foreach (var kvp in raw)
                if (long.TryParse(kvp.Key, out var id)) map[id] = kvp.Value;
            return map;
        }
    }
}
