using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services;

/// <summary>
/// Converts an EOS / Square Enix account export into a NeverCrisis offline server account.json.
/// A port of the community ff7ec_account_parser.py: the server's freshly generated account.json
/// is the identity template (local user id, server metadata) and every account table is rebuilt
/// from the export, each row encoded as protobuf hex using the export's field order.
/// </summary>
public static class NeverCrisisAccountConverter
{
    public sealed record Result(byte[] Json, int Tables, int Rows, int StoneTypes, long? SourceUserId, long TargetUserId);

    // Numeric table IDs, from FF7ECServer.pak/tables_fields.json (via ff7ec_account_parser.py).
    private static readonly Dictionary<int, string> Tables = new()
    {
        [518439670] = "UserAbilityEnemyList", [44724904] = "UserAccessoryList", [216666042] = "UserAccessoryCollectionList",
        [162584964] = "UserCraftList", [26335114] = "UserCraftOptionItemList", [462744031] = "UserAccessoryCraftList",
        [371275198] = "UserAccessoryRecipeList", [184655097] = "UserMaintenanceExtensionList", [7927355] = "UserAdvertisementList",
        [253541454] = "UserAdvertisingSettingList", [78010291] = "UserSoloAreaGroupCategoryList", [262249677] = "UserSoloAreaList",
        [382101703] = "UserSoloAreaBattleList", [328225571] = "UserMultiAreaBattleList", [348037468] = "UserBattleEntryList",
        [398172826] = "UserMultiBattleEntryList", [185321110] = "UserAnotherBattleList", [234757122] = "UserDamageChallengeBattleList",
        [434964626] = "UserBoostList", [11493779] = "UserBossChallengeList", [536518072] = "UserBoxGachaList",
        [376761274] = "UserBoxGachaRewardLimitedList", [177996011] = "UserCharacterCostumeList", [470504869] = "UserCharacterList",
        [70311223] = "UserCharacterStoryEpisodeList", [374566514] = "UserCharacterStoryEpisodeBattleList", [349314943] = "UserChatStampGroupList",
        [199517602] = "UserChocoboList", [243702816] = "UserChocoboExpeditionDeckList", [260221554] = "UserChocoboExpeditionShortenList",
        [77702874] = "UserChocoboFarmList", [152341325] = "UserChocoboEventFarmList", [497630121] = "UserChocoboExpeditionGroupList",
        [479484478] = "UserComebackList", [397640286] = "UserDailyQuestEntryList", [354301834] = "UserDailyQuestList",
        [231374457] = "UserDailyQuestSettingList", [451490019] = "UserAnotherDungeonList", [366015050] = "UserAnotherDungeonEntryList",
        [379417884] = "UserAnotherDungeonExchangeList", [210684459] = "UserDungeonTriggerLockList", [91401000] = "UserDungeonTreasureGroupList",
        [349693898] = "UserDungeonTreasureList", [496869013] = "UserDungeonEntryList", [523291587] = "UserDungeonDramaSelectionList",
        [483522082] = "UserAnotherDungeonSaveList", [211137411] = "UserEventBaseList", [75634232] = "UserEventSoloBattleList",
        [276224501] = "UserEventMultiBattleList", [303529414] = "UserEventScenarioEpisodeList", [420358611] = "UserEventDramaSelectionList",
        [111504963] = "UserEventScoreDungeonEntryList", [144793023] = "UserEventIdlingCollectList", [175031597] = "UserEventMemoriaList",
        [64960203] = "UserEventCrisisBattleList", [231216872] = "UserEventCrisisBattleSoloBattleList", [67246496] = "UserEventDamageRankingBattleSoloBattleList",
        [396476679] = "UserGachaStepGroupList", [131279630] = "UserGachaStampSheetGroupList", [435029244] = "UserGachaStampSheetCellChoiceGroupList",
        [217974652] = "UserGachaWishChoiceList", [482350451] = "UserGrowthBoardGroupList", [520205158] = "SharedGuildBaseList",
        [327371391] = "SharedGuildExpList", [89542649] = "SharedGuildMemberList", [146969950] = "SharedGuildItemList",
        [497080918] = "SharedGuildBonusList", [29830472] = "SharedGuildAchievementList", [412893154] = "SharedGuildShopItemList",
        [172599504] = "SharedGuildMemberSharingItemList", [310604536] = "UserGuildBaseList", [108652072] = "UserGuildPlayRecordList",
        [209286001] = "UserGuildBonusList", [224396503] = "SharedGuildEventGuildRankingBattleList", [191524955] = "SharedGuildEventGuildRankingBaseList",
        [340346445] = "UserEventGuildRankingBattleList", [483254951] = "UserEventGuildRankingBattleEntryList", [282964284] = "UserEventGuildRankingBaseList",
        [111083208] = "UserHighwindList", [415716746] = "UserHighwindPartsList", [142506250] = "UserHighwindKeyItemList",
        [367244463] = "UserHighwindKeyItemEffectGroupList", [39474168] = "UserHighwindBattleReleaseList", [11475233] = "UserHighwindBattleList",
        [182461828] = "UserHomeBackgroundList", [242346576] = "UserHomeBackgroundSettingList", [331261873] = "UserItemList",
        [5165607] = "UserBigItemList", [3131083] = "UserDailyRewardGroupList", [489119532] = "UserLimitedReleaseList",
        [519551592] = "UserLoginBonusList", [19548514] = "UserMateriaList", [419459274] = "UserMateriaCollectionList",
        [84050554] = "UserMateriaCraftList", [32650813] = "UserMateriaRecipeList", [25430143] = "UserMemoriaList",
        [276062282] = "UserMissionGroupList", [249555284] = "UserMissionSetList", [388350056] = "UserMissionList",
        [91028307] = "UserMissionSpecialSummonList", [267483008] = "UserFirstMeetingList", [17062056] = "UserPartyMemberList",
        [312005933] = "UserPartyList", [60548446] = "UserPartyArmouryList", [221515292] = "UserHonorList",
        [327798233] = "UserProfileList", [422068443] = "UserPlayRecordList", [84430053] = "UserAwardList",
        [153868888] = "UserSeasonPassList", [342637010] = "UserShopItemList", [326902235] = "UserShopItemLineupList",
        [292821734] = "UserShopList", [164726483] = "UserSkillSpecialList", [415505131] = "UserSkillOveraccelList",
        [173482689] = "UserSteamAchievementList", [453866343] = "UserEpisodeList", [352926579] = "UserEpisodeEntryList",
        [145638114] = "UserEpisodeBattleList", [78231314] = "UserStoryDramaSelectionList", [417328171] = "UserSummonList",
        [355855709] = "UserTimeList", [24354837] = "UserTowerList", [274829521] = "UserTutorialStepList",
        [67390510] = "UserRecoveryStoneList", [259686066] = "UserStatusList", [348759352] = "UserSettingList",
        [231622239] = "UserWeaponList", [223560503] = "UserWeaponAttachmentEffectSelectingList",
    };

    private static readonly Dictionary<string, int> TableIds = Tables.ToDictionary(kv => kv.Value, kv => kv.Key);

    // Tables keyed only by UserId.
    private static readonly HashSet<string> SingletonTables = new()
    {
        "UserProfileList", "UserStatusList", "UserAdvertisingSettingList", "UserHomeBackgroundSettingList",
    };

    /// <summary>Throws <see cref="InvalidDataException"/> with a user-facing message when an input is unusable.</summary>
    public static Result Convert(Stream exportStream, Stream templateStream)
    {
        using var exportDoc = ParseJson(exportStream, "The EOS export");
        var template = ParseNode(templateStream);

        var source = exportDoc.RootElement;
        if (source.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The EOS export isn't a JSON object.");
        if (template["tables"] is not JsonObject templateTables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json the server created.");
        if (template["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long targetUid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json the server created.");

        JsonElement accountInfo;
        if (source.TryGetProperty("AccountInfo", out var ai) && ai.ValueKind == JsonValueKind.Object)
            accountInfo = ai;
        else if (source.EnumerateObject().Any(p => p.Name.StartsWith("User") || p.Name.StartsWith("Shared")))
            accountInfo = source;
        else
            throw new InvalidDataException("The EOS export has no AccountInfo section.");

        var result = (JsonObject)template.DeepClone();
        var tables = (JsonObject)result["tables"]!;

        // The template only supplies identity; known gameplay tables are rebuilt from the export.
        foreach (var tid in Tables.Keys)
        {
            var key = tid.ToString();
            if (tables.ContainsKey(key)) tables[key] = new JsonObject();
        }

        var freshWide = new HashSet<int>();
        if (template["wide_keys"] is JsonArray wideArr)
            foreach (var w in wideArr)
                if (w is JsonValue wv && wv.TryGetValue(out int wi)) freshWide.Add(wi);
        var resultWide = new HashSet<int>(freshWide);

        int importedTables = 0, encodedRows = 0;
        foreach (var table in accountInfo.EnumerateObject())
        {
            var rowsEl = table.Value;
            if (rowsEl.ValueKind == JsonValueKind.Null) continue;
            if (rowsEl.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"{table.Name}: expected a list.");
            if (rowsEl.GetArrayLength() == 0) continue;
            var rows = rowsEl.EnumerateArray().ToList();
            if (rows.Any(r => r.ValueKind != JsonValueKind.Object))
                throw new InvalidDataException($"{table.Name}: contains non-object rows.");
            if (!TableIds.TryGetValue(table.Name, out var tid))
                throw new InvalidDataException($"{table.Name}: this table isn't known to the converter.");

            var freshRows = templateTables[tid.ToString()] as JsonObject;
            var schema = Schema(rows);
            var keyFields = KeyFields(table.Name, rows, schema, freshRows, freshWide.Contains(tid), targetUid);

            var outRows = new JsonObject();
            var seen = new HashSet<string>();
            try
            {
                foreach (var row in rows)
                {
                    var key = MakeKey(row, schema, keyFields, targetUid);
                    if (!seen.Add(key))
                        throw new InvalidDataException($"duplicate row key {key}");
                    var raw = EncodeMessage(row, schema, targetUid);
                    outRows[key] = System.Convert.ToHexString(raw).ToLowerInvariant();
                    encodedRows++;
                }
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{table.Name}: {ex.Message}");
            }

            tables[tid.ToString()] = outRows;
            importedTables++;
            if (keyFields.Count > 2) resultWide.Add(tid);
        }

        result["user_id"] = targetUid;
        result["other_info"] = template["other_info"]?.DeepClone();
        result["wide_keys"] = new JsonArray(resultWide.Order().Select(w => (JsonNode)w).ToArray());

        int stoneTypes = 0;
        long? sourceUid = null;
        if (source.TryGetProperty("OtherInfo", out var other) && other.ValueKind == JsonValueKind.Object
            && other.TryGetProperty("UserStoneList", out var stoneList) && stoneList.ValueKind == JsonValueKind.Array)
        {
            var stones = new JsonObject();
            foreach (var s in stoneList.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                if (sourceUid is null && s.TryGetProperty("UserId", out var su) && su.TryGetInt64(out var suv)) sourceUid = suv;
                if (s.TryGetProperty("StoneId", out var sid) && sid.TryGetInt64(out var sidv)
                    && s.TryGetProperty("Count", out var cnt) && cnt.TryGetInt64(out var cntv))
                    stones[sidv.ToString()] = cntv;
            }
            if (stones.Count > 0)
            {
                result["stones"] = stones;
                stoneTypes = stones.Count;
            }
        }
        sourceUid ??= FirstUserId(accountInfo);

        var json = result.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n"; // indented output uses the OS newline; values never contain a raw CR

        return new Result(Encoding.UTF8.GetBytes(json), importedTables, encodedRows, stoneTypes, sourceUid, targetUid);
    }

    public sealed record GearResult(byte[] Json, List<GearOption> Added, List<GearOption> AlreadyOwned);

    /// <summary>
    /// Adds outfits and weapons to an existing NeverCrisis account.json. Weapons are added as a fresh
    /// 5★ copy (level 1, no overboost), the same state as a new pull. Gear already owned is left alone.
    /// </summary>
    public static GearResult AddGear(Stream accountStream, IEnumerable<GearOption> gear)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var added = new List<GearOption>();
        var owned = new List<GearOption>();
        foreach (var g in gear.DistinctBy(g => (g.Type, g.Id)))
        {
            var tid = TableIds[g.Type == GearType.Outfit ? "UserCharacterCostumeList" : "UserWeaponList"].ToString();
            if (tables[tid] is not JsonObject rows)
                tables[tid] = rows = new JsonObject();

            // Row keys look like "1:<uid>|2:<id>"; field 2 is the costume / weapon id.
            var idPart = $"2:{g.Id}";
            if (rows.Any(r => r.Key.Split('|').Contains(idPart)))
            {
                owned.Add(g);
                continue;
            }

            var buf = new List<byte>();
            void Field(int no, ulong v) { if (v != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, v); } }
            Field(1, (ulong)uid);
            Field(2, (ulong)g.Id);
            if (g.Type == GearType.Weapon)
            {
                Field(3, (ulong)g.RarityType); // RarityType: 3 = 5★, 101 = Ultimate
                Field(4, 1);                    // WeaponUpgradeType: normal
                Field(8, now);                  // GetDatetime
            }
            rows[$"1:{uid}|{idPart}"] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            added.Add(g);
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new GearResult(Encoding.UTF8.GetBytes(json), added, owned);
    }

    public sealed record EscalationChange(EscalationOption Challenge, int OldLevel, int NewLevel);

    public sealed record EscalationResult(byte[] Json, List<EscalationChange> Changed);

    // Sets the cleared stage (ClearLevel) of Escalation Challenges in a NeverCrisis account.json.
    // Rows are UserId(1), BossChallengeId(2), ClearLevel(3); the game unlocks later stages from ClearLevel.
    public static EscalationResult SetEscalation(Stream accountStream, IEnumerable<(EscalationOption Challenge, int Level)> levels)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var tid = TableIds["UserBossChallengeList"].ToString();
        if (tables[tid] is not JsonObject rows)
            tables[tid] = rows = new JsonObject();

        // Current ClearLevel per challenge, keyed by the row key it lives under.
        var current = new Dictionary<long, (string Key, int Level)>();
        foreach (var (key, value) in rows)
        {
            if (value is not JsonValue v || !v.TryGetValue(out string? hex)) continue;
            var fields = DecodeVarints(System.Convert.FromHexString(hex));
            current[(long)fields.GetValueOrDefault(2)] = (key, (int)fields.GetValueOrDefault(3));
        }

        var changed = new List<EscalationChange>();
        foreach (var (challenge, level) in levels.DistinctBy(l => l.Challenge.Id))
        {
            var existing = current.GetValueOrDefault(challenge.Id);
            if (existing.Key is not null && existing.Level == level) continue;
            var buf = new List<byte>();
            void Field(int no, ulong v) { if (v != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, v); } }
            Field(1, (ulong)uid);
            Field(2, (ulong)challenge.Id);
            Field(3, (ulong)level);
            rows[existing.Key ?? $"1:{uid}|2:{challenge.Id}"] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed.Add(new EscalationChange(challenge, existing.Level, level));
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new EscalationResult(Encoding.UTF8.GetBytes(json), changed);
    }

    /// <summary>Requested state for one Highwind collection item; null fields are left unchanged.</summary>
    public sealed record HighwindRequest(HighwindItemOption Item, bool Own, int? Upgrade, IReadOnlyDictionary<int, int> BonusSteps);

    public sealed record HighwindResult(byte[] Json, List<string> Changed);

    // Sets Highwind collection items in a NeverCrisis account.json:
    //   UserHighwindKeyItemList            UserId(1), HighwindKeyItemId(2), GetDatetime(3), RankUpgradeType(4), UpgradeCount(5)
    //   UserHighwindKeyItemEffectGroupList UserId(1), HighwindKeyItemEffectGroupId(2), ReceivedHighwindKeyItemEffectGroupIdxFlags0..19(3..22)
    // A bonus step is received when bit <step index> is set (flags word = index / 64, bit = index % 64).
    public static HighwindResult SetHighwind(Stream accountStream, IEnumerable<HighwindRequest> requests)
    {
        const int FlagWords = 20;
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        JsonObject Table(string name)
        {
            var tid = TableIds[name].ToString();
            if (tables[tid] is not JsonObject rows) tables[tid] = rows = new JsonObject();
            return rows;
        }
        static Dictionary<long, (string Key, Dictionary<int, ulong> Fields)> Index(JsonObject rows)
        {
            var map = new Dictionary<long, (string, Dictionary<int, ulong>)>();
            foreach (var (key, value) in rows)
                if (value is JsonValue v && v.TryGetValue(out string? hex))
                {
                    var f = DecodeVarints(System.Convert.FromHexString(hex));
                    map[(long)f.GetValueOrDefault(2)] = (key, f);
                }
            return map;
        }
        static string Encode(IEnumerable<(int No, ulong Value)> fields)
        {
            var buf = new List<byte>();
            foreach (var (no, v) in fields)
                if (v != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, v); }
            return System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
        }

        var itemRows = Table("UserHighwindKeyItemList");
        var groupRows = Table("UserHighwindKeyItemEffectGroupList");
        var items = Index(itemRows);
        var groups = Index(groupRows);
        // UserMissionList: UserId(1), MissionId(2), ProgressCount(3), ReceivedProgressCount(4), NotifiedProgressCount(5)
        // UserMissionGroupList: UserId(1), MissionGroupId(2), ..., LastResetDatetime(10)
        var missionRows = Table("UserMissionList");
        var missionGroupRows = Table("UserMissionGroupList");
        var missions = Index(missionRows);
        var missionGroups = Index(missionGroupRows);
        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changed = new List<string>();

        foreach (var r in requests.DistinctBy(r => r.Item.Id))
        {
            var item = r.Item;
            var notes = new List<string>();
            var owned = items.TryGetValue(item.Id, out var row);
            var wantsChange = r.Own || r.Upgrade is not null || r.BonusSteps.Count > 0;
            if (!wantsChange) continue;

            // Collection item row: add it if missing, then apply the overboost count.
            var fields = owned ? row.Fields : new Dictionary<int, ulong> { [1] = (ulong)uid, [2] = (ulong)item.Id, [3] = now, [4] = 1 };
            var oldUpgrade = (int)fields.GetValueOrDefault(5);
            if (r.Upgrade is int up) fields[5] = (ulong)up;
            if (!owned) notes.Add("added");
            if ((int)fields.GetValueOrDefault(5) != oldUpgrade) notes.Add($"overboost {oldUpgrade} → {fields[5]}");
            if (!owned || notes.Count > 0)
            {
                fields[1] = (ulong)uid;
                itemRows[owned ? row.Key : $"1:{uid}|2:{item.Id}"] =
                    Encode(Enumerable.Range(1, 5).Select(n => (n, fields.GetValueOrDefault(n))));
            }

            // Bonus steps: for each stat, mark the first N steps received and clear the rest.
            var hasGroup = groups.TryGetValue(item.EffectGroupId, out var g);
            var flags = new ulong[FlagWords];
            if (hasGroup)
                for (var w = 0; w < FlagWords; w++) flags[w] = g.Fields.GetValueOrDefault(3 + w);
            var before = (ulong[])flags.Clone();
            foreach (var bonus in item.Bonuses)
            {
                if (!r.BonusSteps.TryGetValue(bonus.Type, out var count)) continue;
                var was = bonus.Indices.Count(i => (flags[i / 64] >> (i % 64) & 1) != 0);
                if (was == Math.Min(count, bonus.Indices.Count)) continue;
                for (var n = 0; n < bonus.Indices.Count; n++)
                {
                    var i = bonus.Indices[n];
                    if (n < count) flags[i / 64] |= 1UL << (i % 64);
                    else flags[i / 64] &= ~(1UL << (i % 64));
                }
                notes.Add($"{bonus.Name} {was} → {count}/{bonus.Indices.Count}");
            }
            if (!flags.SequenceEqual(before) || (!hasGroup && !owned))
            {
                groupRows[hasGroup ? g.Key : $"1:{uid}|2:{item.EffectGroupId}"] =
                    Encode(new[] { (1, (ulong)uid), (2, (ulong)item.EffectGroupId) }.Concat(flags.Select((f, w) => (3 + w, f))));
            }

            // Each bonus step is a reward of the item's upgrade missions. Keep those missions in step with the
            // received bits: a milestone counts as claimed when it and every milestone before it are received.
            bool Received(int i) => (flags[i / 64] >> (i % 64) & 1) != 0;
            var missionsSynced = 0;
            foreach (var mission in item.Missions)
            {
                var claimed = 0;
                foreach (var stone in mission.Milestones)
                {
                    if (!stone.Indices.All(Received)) break;
                    claimed = stone.Progress;
                }
                var has = missions.TryGetValue(mission.Id, out var mrow);
                var mf = has ? mrow.Fields : new Dictionary<int, ulong>();
                var progress = Math.Max((int)mf.GetValueOrDefault(3), claimed);
                if (has ? (int)mf.GetValueOrDefault(4) == claimed && (int)mf.GetValueOrDefault(3) == progress : claimed == 0) continue;
                mf[1] = (ulong)uid;
                mf[2] = (ulong)mission.Id;
                mf[3] = (ulong)progress;
                mf[4] = (ulong)claimed;
                missionRows[has ? mrow.Key : $"1:{uid}|2:{mission.Id}"] = Encode(mf.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)));
                missionsSynced++;
            }
            if (missionsSynced > 0 && item.MissionGroupId != 0 && !missionGroups.ContainsKey(item.MissionGroupId))
            {
                missionGroupRows[$"1:{uid}|2:{item.MissionGroupId}"] = Encode(new[] { (1, (ulong)uid), (2, (ulong)item.MissionGroupId), (10, now) });
                missionGroups[item.MissionGroupId] = ($"1:{uid}|2:{item.MissionGroupId}", new());
            }
            if (missionsSynced > 0) notes.Add($"{missionsSynced} upgrade mission(s) set to match");

            if (notes.Count > 0) changed.Add($"{item.Name}: {string.Join(", ", notes)}");
        }

        // The Highwind opens at player rank 20 (UserRank: 36,200 exp, 135 stamina max). Raise lower accounts to it.
        const ulong Rank20Exp = 36200, Rank20Stamina = 135;
        if (changed.Count > 0)
        {
            var statusRows = Table("UserStatusList");
            var statusKey = $"1:{uid}";
            var sf = statusRows[statusKey] is JsonValue sv && sv.TryGetValue(out string? shex)
                ? DecodeVarints(System.Convert.FromHexString(shex))
                : new Dictionary<int, ulong>();
            if (sf.GetValueOrDefault(2) < Rank20Exp)
            {
                sf[1] = (ulong)uid;
                sf[2] = Rank20Exp;
                sf[3] = Math.Max(sf.GetValueOrDefault(3), Rank20Stamina);
                if (sf.GetValueOrDefault(4) == 0) sf[4] = now;
                statusRows[statusKey] = Encode(sf.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)));
                changed.Add("Player rank raised to 20 so the Highwind can be opened");
            }
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new HighwindResult(Encoding.UTF8.GetBytes(json), changed);
    }

    public sealed record MemoriaResult(byte[] Json, List<string> Changed);

    // Adds memoria to a NeverCrisis account.json, or sets the level of ones already owned.
    // UserMemoriaList: UserId(1), MemoriaId(2), FragmentCount(3), AnalysisPoint(4), FragmentFirstGetDatetime(5), EquipableDatetime(6).
    // A memoria is owned once EquipableDatetime is set; its level (and skill levels) follow from AnalysisPoint.
    public static MemoriaResult SetMemoria(Stream accountStream, IEnumerable<(MemoriaOption Memoria, int Level)> levels)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var tid = TableIds["UserMemoriaList"].ToString();
        if (tables[tid] is not JsonObject rows)
            tables[tid] = rows = new JsonObject();
        var current = new Dictionary<long, (string Key, Dictionary<int, ulong> Fields)>();
        foreach (var (key, value) in rows)
            if (value is JsonValue v && v.TryGetValue(out string? hex))
            {
                var f = DecodeVarints(System.Convert.FromHexString(hex));
                current[(long)f.GetValueOrDefault(2)] = (key, f);
            }

        static int LevelOf(MemoriaOption m, ulong points) => m.LevelPoints.Count(p => (ulong)p <= points);

        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changed = new List<string>();
        foreach (var (memoria, level) in levels.DistinctBy(l => l.Memoria.Id))
        {
            var has = current.TryGetValue(memoria.Id, out var row);
            var fields = has ? row.Fields : new Dictionary<int, ulong>();
            var owned = fields.GetValueOrDefault(6) != 0;
            var oldLevel = owned ? LevelOf(memoria, fields.GetValueOrDefault(4)) : 0;
            if (owned && oldLevel == level) continue;

            fields[1] = (ulong)uid;
            fields[2] = (ulong)memoria.Id;
            fields[3] = Math.Max(fields.GetValueOrDefault(3), (ulong)memoria.Fragments);
            fields[4] = (ulong)memoria.LevelPoints[level - 1];
            if (fields.GetValueOrDefault(5) == 0) fields[5] = now;
            if (fields.GetValueOrDefault(6) == 0) fields[6] = now;

            var buf = new List<byte>();
            foreach (var (no, v) in fields.OrderBy(kv => kv.Key))
                if (v != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, v); }
            rows[has ? row.Key : $"1:{uid}|2:{memoria.Id}"] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed.Add(owned ? $"{memoria.Name}: level {oldLevel} → {level}" : $"{memoria.Name}: added at level {level}");
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new MemoriaResult(Encoding.UTF8.GetBytes(json), changed);
    }

    public sealed record MateriaResult(byte[] Json, List<string> Changed);

    // Adds 5★ materia to a NeverCrisis account.json.
    // UserMateriaList: UserId(1), UserMateriaId(2), MateriaId(3), QualityType(4), EvolveCount(5), NotesSetId(6), Exp(7), IsLock(8),
    //   GetDatetime(9), ParameterOpenCount(10), ParameterId0-3(11-14), ParameterValue0-3(15-18).
    // UserMateriaCollectionList (per evolve step): UserId(1), MateriaEvolveId(2), Quality1-5ObtainCount(3-7), ..., MaxLevel(14).
    public static MateriaResult SetMateria(Stream accountStream, IEnumerable<MateriaAddRequest> requests)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        JsonObject Table(string name)
        {
            var tid = TableIds[name].ToString();
            if (tables[tid] is not JsonObject rows) tables[tid] = rows = new JsonObject();
            return rows;
        }
        static string Encode(IEnumerable<(int No, ulong Value)> fields)
        {
            var buf = new List<byte>();
            foreach (var (no, v) in fields)
                if (v != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, v); }
            return System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
        }

        var materiaRows = Table("UserMateriaList");
        var collectionRows = Table("UserMateriaCollectionList");
        ulong nextId = 0;
        foreach (var (_, value) in materiaRows)
            if (value is JsonValue v && v.TryGetValue(out string? hex))
                nextId = Math.Max(nextId, DecodeVarints(System.Convert.FromHexString(hex)).GetValueOrDefault(2));
        nextId = nextId == 0 ? (ulong)Random.Shared.NextInt64(1L << 58, 1L << 59) : nextId + 1;

        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changed = new List<string>();
        foreach (var req in requests)
        {
            var m = req.Materia;
            var evolve = m.EvolveAt(req.Level);
            var fields = new List<(int, ulong)>
            {
                (1, (ulong)uid), (2, nextId), (3, (ulong)m.MateriaId), (4, 5), (5, (ulong)evolve), (6, (ulong)req.Notes),
                (7, (ulong)m.ExpByLevel[req.Level - 1]), (9, now), (10, (ulong)req.Stats.Count),
            };
            for (var i = 0; i < req.Stats.Count; i++) fields.Add((11 + i, (ulong)req.Stats[i].Id));
            for (var i = 0; i < req.Stats.Count; i++) fields.Add((15 + i, (ulong)req.Stats[i].Value));
            materiaRows[$"1:{uid}|2:{nextId}"] = Encode(fields);
            nextId++;

            // The materia list (collection) records each evolve step reached and its highest level.
            for (var e = 0; e <= evolve; e++)
            {
                var evolveId = m.EvolveIds[e];
                var key = $"1:{uid}|2:{evolveId}";
                var f = collectionRows[key] is JsonValue cv && cv.TryGetValue(out string? chex)
                    ? DecodeVarints(System.Convert.FromHexString(chex))
                    : new Dictionary<int, ulong>();
                f[1] = (ulong)uid;
                f[2] = (ulong)evolveId;
                if (e == evolve) f[7] = f.GetValueOrDefault(7) + 1;
                f[14] = Math.Max(f.GetValueOrDefault(14), (ulong)Math.Min(req.Level, m.Caps[e]));
                collectionRows[key] = Encode(f.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)));
            }

            var sigil = req.Notes switch { 1 => " ⬤", 2 => " ▲", 3 => " ✖", _ => string.Empty };
            changed.Add(m.Refined ? $"{m.Name}{sigil}" : $"{m.Name} Lv {req.Level}");
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new MateriaResult(Encoding.UTF8.GetBytes(json), changed);
    }

    public sealed record BrandResult(byte[] Json, List<string> Changed);

    // Writes brand lines onto owned weapons in a NeverCrisis account.json. UserWeaponList rows keep
    // WeaponAttachmentEffectId0-2 in fields 16-18; an empty slot is left out (zero).
    public static BrandResult SetBrands(Stream accountStream, IEnumerable<WeaponBrandRequest> requests)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long _))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var weaponRows = tables[TableIds["UserWeaponList"].ToString()] as JsonObject ?? new JsonObject();
        var rowsById = new Dictionary<ulong, (string Key, Dictionary<int, ulong> Fields)>();
        foreach (var (key, value) in weaponRows)
            if (value is JsonValue v && v.TryGetValue(out string? hex))
            {
                var f = DecodeVarints(System.Convert.FromHexString(hex));
                rowsById[f.GetValueOrDefault(2)] = (key, f);
            }

        var changed = new List<string>();
        foreach (var req in requests)
        {
            if (!rowsById.TryGetValue((ulong)req.WeaponId, out var row))
                throw new InvalidDataException($"The account doesn't own {req.WeaponName}, so it can't be branded. Add the weapon first.");
            var f = row.Fields;
            var before = (f.GetValueOrDefault(16), f.GetValueOrDefault(17), f.GetValueOrDefault(18));
            for (var i = 0; i < 3; i++)
            {
                if (i < req.EffectIds.Count) f[16 + i] = (ulong)req.EffectIds[i];
                else f.Remove(16 + i);
            }
            if (before == (f.GetValueOrDefault(16), f.GetValueOrDefault(17), f.GetValueOrDefault(18))) continue;
            var buf = new List<byte>();
            foreach (var (no, val) in f.OrderBy(kv => kv.Key))
                if (val != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, val); }
            weaponRows[row.Key] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed.Add(req.WeaponName);
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new BrandResult(Encoding.UTF8.GetBytes(json), changed);
    }

    public sealed record RankResult(byte[] Json, long OldExp, long NewExp, ulong OldStamina, ulong NewStamina);

    // Sets the player's rank in a NeverCrisis account.json. UserStatusList: UserId(1), Exp(2), StaminaCount(3),
    // StaminaLastRecoveredDatetime(4); the rank follows from Exp. A stamina value fills stamina to that amount.
    public static RankResult SetPlayerRank(Stream accountStream, long exp, int? stamina)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var tid = TableIds["UserStatusList"].ToString();
        if (tables[tid] is not JsonObject rows) tables[tid] = rows = new JsonObject();
        var key = $"1:{uid}";
        var f = rows[key] is JsonValue v && v.TryGetValue(out string? hex)
            ? DecodeVarints(System.Convert.FromHexString(hex))
            : new Dictionary<int, ulong>();
        var oldExp = (long)f.GetValueOrDefault(2);
        var oldStamina = f.GetValueOrDefault(3);
        f[1] = (ulong)uid;
        f[2] = (ulong)exp;
        if (stamina is int s) f[3] = (ulong)s;
        if (f.GetValueOrDefault(4) == 0) f[4] = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var buf = new List<byte>();
        foreach (var (no, val) in f.OrderBy(kv => kv.Key))
            if (val != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, val); }
        rows[key] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new RankResult(Encoding.UTF8.GetBytes(json), oldExp, exp, oldStamina, f.GetValueOrDefault(3));
    }

    public sealed record WeaponProgressResult(byte[] Json, List<string> Changed);

    // Sets rarity, level cap, level and overboost on owned weapons in a NeverCrisis account.json.
    // UserWeaponList: RarityType(3), WeaponUpgradeType(4), UpgradeCount(5), ReleaseCount(6), Exp(7), RarityUpCount(13).
    public static WeaponProgressResult SetWeaponProgress(Stream accountStream, IEnumerable<WeaponProgressRequest> requests)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long _))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var weaponRows = tables[TableIds["UserWeaponList"].ToString()] as JsonObject ?? new JsonObject();
        var rowsById = new Dictionary<ulong, (string Key, Dictionary<int, ulong> Fields)>();
        foreach (var (key, value) in weaponRows)
            if (value is JsonValue v && v.TryGetValue(out string? hex))
            {
                var f = DecodeVarints(System.Convert.FromHexString(hex));
                rowsById[f.GetValueOrDefault(2)] = (key, f);
            }

        var changed = new List<string>();
        foreach (var req in requests)
        {
            if (!rowsById.TryGetValue((ulong)req.WeaponId, out var row))
                throw new InvalidDataException($"The account doesn't own {req.WeaponName}. Add the weapon first.");
            var f = row.Fields;
            var before = (f.GetValueOrDefault(3), f.GetValueOrDefault(4), f.GetValueOrDefault(5), f.GetValueOrDefault(6), f.GetValueOrDefault(7));
            // RarityUpCount(13) records how many times the weapon's rarity was raised, as the game does when stars are added.
            var oldRarity = (long)f.GetValueOrDefault(3);
            if (oldRarity is >= 1 and <= 3 && req.Rarity is >= 1 and <= 3 && req.Rarity != oldRarity)
                f[13] = (ulong)Math.Max(0, (long)f.GetValueOrDefault(13) + req.Rarity - oldRarity);
            f[3] = (ulong)req.Rarity;
            f[4] = (ulong)req.UpgradeType;
            f[5] = (ulong)req.UpgradeCount;
            f[6] = (ulong)req.ReleaseCount;
            f[7] = (ulong)req.Exp;
            if (before == (f.GetValueOrDefault(3), f.GetValueOrDefault(4), f.GetValueOrDefault(5), f.GetValueOrDefault(6), f.GetValueOrDefault(7))) continue;
            var buf = new List<byte>();
            foreach (var (no, val) in f.OrderBy(kv => kv.Key))
                if (val != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, val); }
            weaponRows[row.Key] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed.Add(req.WeaponName);
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new WeaponProgressResult(Encoding.UTF8.GetBytes(json), changed);
    }

    public sealed record ChocoboResult(byte[] Json, int Added, bool RanchSetUp);

    // Adds chocobos to a NeverCrisis account.json.
    // UserChocoboList: UserId(1), UserChocoboId(2), ChocoboId(3), ChocoboRankGroupId(4), ChocoboExpeditionParameterGroupId(5),
    //   RankType(6), LimitRankType(7), AreaTypeFlags(8), ChocoboType(9), SexType(10), Name(11, string), Speed/Stamina/
    //   Intelligence/AdaptabilityWeight(12-15), current stats and rank-up totals (16-27, 0 on a new bird), feeding (28-30),
    //   IsLock(31), GetDatetime(32).
    public static ChocoboResult AddChocobos(Stream accountStream, IEnumerable<ChocoboAddRequest> requests)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var tableKey = TableIds["UserChocoboList"].ToString();
        if (tables[tableKey] is not JsonObject rows) tables[tableKey] = rows = new JsonObject();
        ulong nextId = 0;
        foreach (var (_, value) in rows)
            if (value is JsonValue v && v.TryGetValue(out string? hex))
                nextId = Math.Max(nextId, DecodeVarints(System.Convert.FromHexString(hex)).GetValueOrDefault(2));
        nextId = nextId == 0 ? (ulong)Random.Shared.NextInt64(1L << 58, 1L << 59) : nextId + 1;

        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ranchSetUp = EnsureChocoboRanch(tables, uid, now);
        var added = 0;
        foreach (var req in requests)
        {
            var c = req.Chocobo;
            var fields = new List<(int No, ulong Value)>
            {
                (1, (ulong)uid), (2, nextId), (3, (ulong)c.Id), (4, (ulong)c.RankGroup), (5, (ulong)c.ExpeditionGroup),
                (6, (ulong)c.Rank), (7, (ulong)c.LimitRank), (8, (ulong)c.Area), (9, (ulong)c.Type), (10, (ulong)c.Sex),
            };
            for (var i = 0; i < 4; i++) fields.Add((12 + i, (ulong)req.Weights[i]));
            fields.Add((32, now));
            var buf = new List<byte>();
            foreach (var (no, val) in fields)
                if (val != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, val); }
            rows[$"1:{uid}|2:{nextId}"] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            nextId++;
            added++;
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new ChocoboResult(Encoding.UTF8.GetBytes(json), added, ranchSetUp);
    }

    public sealed record StoryResult(byte[] Json, int Chapters, int Episodes);

    // Marks story chapters cleared. Main story episodes go in UserEpisodeList (UserId 1, StoryEpisodeId 2,
    // StoryPartyModeClearDatetime 3, FreePartyModeClearDatetime 4) with their battles in UserEpisodeBattleList
    // (UserId, StoryEpisodeId, StoryBattleId). Character story episodes go in UserCharacterStoryEpisodeList
    // (UserId, CharacterStoryEpisodeId) and UserCharacterStoryEpisodeBattleList (UserId, CharacterStoryEpisodeId, BattleId).
    // Episodes already cleared are left as they are. First-clear rewards are not granted.
    public static StoryResult SetStoryProgress(Stream accountStream, IEnumerable<StoryChapterOption> chapters)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        JsonObject Table(string name)
        {
            var key = TableIds[name].ToString();
            if (tables[key] is not JsonObject rows) tables[key] = rows = new JsonObject();
            return rows;
        }
        string Encode(params ulong[] values)
        {
            var buf = new List<byte>();
            for (var i = 0; i < values.Length; i++)
                if (values[i] != 0) { WriteVarint(buf, (ulong)((i + 1) << 3)); WriteVarint(buf, values[i]); }
            return System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
        }

        var mainEpisodes = Table("UserEpisodeList");
        var mainBattles = Table("UserEpisodeBattleList");
        var charEpisodes = Table("UserCharacterStoryEpisodeList");
        var charBattles = Table("UserCharacterStoryEpisodeBattleList");
        var time = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var done = 0;
        var count = 0;
        foreach (var chapter in chapters.DistinctBy(c => c.Key))
        {
            var (episodes, battles) = chapter.Kind == 1 ? (mainEpisodes, mainBattles) : (charEpisodes, charBattles);
            for (var i = 0; i < chapter.Episodes.Length; i++)
            {
                var ep = (ulong)chapter.Episodes[i];
                var key = $"1:{uid}|2:{ep}";
                if (episodes[key] is null)
                {
                    episodes[key] = chapter.Kind == 1 ? Encode((ulong)uid, ep, time++) : Encode((ulong)uid, ep);
                    count++;
                }
                foreach (var b in chapter.Battles[i])
                    battles[$"1:{uid}|2:{ep}|3:{b}"] ??= Encode((ulong)uid, ep, (ulong)b);
            }
            done++;
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new StoryResult(Encoding.UTF8.GetBytes(json), done, count);
    }

    // A converted account carries the ranch records the live game created when the player first opened the chocobo
    // farm; an account started on the offline server can lack them, and the farm screen then never finishes loading.
    // Adds the farm record (UserChocoboFarmList: UserId, LastOpenTreasureDatetime) and the three expedition team slots
    // (UserChocoboExpeditionDeckList: UserId, ChocoboExpeditionDeckId, rest empty) and the expedition area groups when
    // they're missing.
    private static bool EnsureChocoboRanch(JsonObject tables, long uid, ulong now)
    {
        var changed = false;
        var farmKey = TableIds["UserChocoboFarmList"].ToString();
        if (tables[farmKey] is not JsonObject farm) tables[farmKey] = farm = new JsonObject();
        if (farm.Count == 0)
        {
            var buf = new List<byte>();
            WriteVarint(buf, 1 << 3); WriteVarint(buf, (ulong)uid);
            WriteVarint(buf, 2 << 3); WriteVarint(buf, now);
            farm[$"1:{uid}|2:{now}"] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed = true;
        }
        var deckKey = TableIds["UserChocoboExpeditionDeckList"].ToString();
        if (tables[deckKey] is not JsonObject decks) tables[deckKey] = decks = new JsonObject();
        for (ulong deck = 1; deck <= 3; deck++)
        {
            var key = $"1:{uid}|2:{deck}";
            if (decks[key] is not null) continue;
            var buf = new List<byte>();
            WriteVarint(buf, 1 << 3); WriteVarint(buf, (ulong)uid);
            WriteVarint(buf, 2 << 3); WriteVarint(buf, deck);
            decks[key] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed = true;
        }
        // One record per expedition area group (UserChocoboExpeditionGroupList: UserId, ChocoboExpeditionGroupId,
        // progress flags left empty). Without them the server drops the chocobos and the farm never loads.
        // Groups 1-23 are the ones the live game created; confirmed in game 2026-10-08.
        var groupKey = TableIds["UserChocoboExpeditionGroupList"].ToString();
        if (tables[groupKey] is not JsonObject groups) tables[groupKey] = groups = new JsonObject();
        for (ulong group = 1; group <= 23; group++)
        {
            var key = $"1:{uid}|2:{group}";
            if (groups[key] is not null) continue;
            var buf = new List<byte>();
            WriteVarint(buf, 1 << 3); WriteVarint(buf, (ulong)uid);
            WriteVarint(buf, 2 << 3); WriteVarint(buf, group);
            groups[key] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed = true;
        }
        return changed;
    }

    public sealed record TutorialResult(byte[] Json, int Changed);

    // Marks every tutorial finished: UserTutorialStepList (UserId 1, TutorialStepType 2, CurrentStep 3) gets each
    // step type at its last step. A converted account has them all finished; an account started on the offline server
    // only has the ones played, and an unplayed tutorial can stop a screen (such as the chocobo farm) from loading.
    public static TutorialResult CompleteTutorials(Stream accountStream, IReadOnlyDictionary<int, int> lastSteps)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long uid))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var tableKey = TableIds["UserTutorialStepList"].ToString();
        if (tables[tableKey] is not JsonObject rows) tables[tableKey] = rows = new JsonObject();
        var current = new Dictionary<ulong, (string Key, ulong Step)>();
        foreach (var (key, value) in rows)
            if (value is JsonValue v && v.TryGetValue(out string? hex))
            {
                var f = DecodeVarints(System.Convert.FromHexString(hex));
                current[f.GetValueOrDefault(2)] = (key, f.GetValueOrDefault(3));
            }
        var changed = 0;
        foreach (var (type, step) in lastSteps)
        {
            var t = (ulong)type;
            if (current.TryGetValue(t, out var have) && have.Step >= (ulong)step) continue;
            var buf = new List<byte>();
            WriteVarint(buf, 1 << 3); WriteVarint(buf, (ulong)uid);
            WriteVarint(buf, 2 << 3); WriteVarint(buf, t);
            WriteVarint(buf, 3 << 3); WriteVarint(buf, (ulong)step);
            rows[have.Key ?? $"1:{uid}|2:{t}"] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed++;
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new TutorialResult(Encoding.UTF8.GetBytes(json), changed);
    }

    public sealed record MateriaDeleteResult(byte[] Json, int Deleted, List<string> Skipped, long GilAdded);

    // Removes materia rows from UserMateriaList. Locked materia and materia equipped in a party
    // (UserPartyMemberList ActiveMateriaId0-2 = fields 7-9) are left alone. The materia list
    // (UserMateriaCollectionList) keeps its history. When saleGil is given, the Gil the game pays for
    // each deleted materia is added to the Gil item, up to gilMax.
    public static MateriaDeleteResult DeleteMateria(Stream accountStream, IEnumerable<ulong> userMateriaIds,
        Func<IReadOnlyDictionary<int, ulong>, long>? saleGil = null, long gilItemId = 0, long gilMax = long.MaxValue)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");

        var equipped = new HashSet<ulong>();
        if (tables[TableIds["UserPartyMemberList"].ToString()] is JsonObject partyRows)
            foreach (var (_, value) in partyRows)
                if (value is JsonValue v && v.TryGetValue(out string? hex))
                {
                    var f = DecodeVarints(System.Convert.FromHexString(hex));
                    for (var no = 7; no <= 9; no++)
                        if (f.GetValueOrDefault(no) is var id and > 0) equipped.Add(id);
                }

        var wanted = userMateriaIds.ToHashSet();
        var skipped = new List<string>();
        var deleted = 0;
        long gil = 0;
        if (tables[TableIds["UserMateriaList"].ToString()] is JsonObject materiaRows)
            foreach (var (key, value) in materiaRows.ToList())
            {
                if (value is not JsonValue v || !v.TryGetValue(out string? hex)) continue;
                var f = DecodeVarints(System.Convert.FromHexString(hex));
                var id = f.GetValueOrDefault(2);
                if (!wanted.Remove(id)) continue;
                if (f.GetValueOrDefault(8) != 0) { skipped.Add($"{id}: locked"); continue; }
                if (equipped.Contains(id)) { skipped.Add($"{id}: equipped in a party"); continue; }
                materiaRows.Remove(key);
                deleted++;
                if (saleGil != null) gil += Math.Max(0, saleGil(f));
            }
        skipped.AddRange(wanted.Select(id => $"{id}: not in this account"));

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);

        long added = 0;
        if (gil > 0 && gilItemId > 0)
        {
            long current = 0;
            if (tables[TableIds["UserItemList"].ToString()] is JsonObject itemRows)
                foreach (var (_, value) in itemRows)
                    if (value is JsonValue v && v.TryGetValue(out string? hex))
                    {
                        var f = DecodeVarints(System.Convert.FromHexString(hex));
                        if (f.GetValueOrDefault(2) == (ulong)gilItemId) current = (long)f.GetValueOrDefault(3);
                    }
            var target = Math.Min(gilMax, current + gil);
            added = Math.Max(0, target - current);
            if (added > 0)
                bytes = SetItemCounts(new MemoryStream(bytes), new[] { new ItemCountRequest(gilItemId, "Gil", target) }).Json;
        }
        return new MateriaDeleteResult(bytes, deleted, skipped, added);
    }

    public sealed record ItemCountResult(byte[] Json, List<string> Changed);

    // Sets item counts in a NeverCrisis account.json, adding rows for items it doesn't have yet.
    // UserItemList: UserId(1), ItemId(2), Count(3), ObtainedTotal(5), UsedTotal(8), FirstGot(10), Updated(11).
    public static ItemCountResult SetItemCounts(Stream accountStream, IEnumerable<ItemCountRequest> requests)
    {
        var account = ParseNode(accountStream);
        if (account["tables"] is not JsonObject tables)
            throw new InvalidDataException("The account.json file doesn't look like a NeverCrisis account (no \"tables\"). Use the data\\account.json from the server.");
        if (account["user_id"] is not JsonValue uidValue || !uidValue.TryGetValue(out long userId))
            throw new InvalidDataException("The account.json file has no user_id. Use the data\\account.json from the server.");

        var tableKey = TableIds["UserItemList"].ToString();
        if (tables[tableKey] is not JsonObject itemRows)
            tables[tableKey] = itemRows = new JsonObject();
        var rowsById = new Dictionary<ulong, (string Key, Dictionary<int, ulong> Fields)>();
        foreach (var (key, value) in itemRows)
            if (value is JsonValue v && v.TryGetValue(out string? hex))
            {
                var f = DecodeVarints(System.Convert.FromHexString(hex));
                rowsById[f.GetValueOrDefault(2)] = (key, f);
            }

        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changed = new List<string>();
        foreach (var req in requests)
        {
            var count = (ulong)Math.Max(0, req.Count);
            if (!rowsById.TryGetValue((ulong)req.ItemId, out var row))
            {
                if (count == 0) continue;
                row = ($"1:{userId}|2:{req.ItemId}", new Dictionary<int, ulong> { [1] = (ulong)userId, [2] = (ulong)req.ItemId, [10] = now });
            }
            var f = row.Fields;
            var old = f.GetValueOrDefault(3);
            if (old == count && itemRows.ContainsKey(row.Key)) continue;
            // The obtained total only grows; a lower count is treated as items used.
            if (count > old) f[5] = f.GetValueOrDefault(5) + count - old;
            else f[8] = f.GetValueOrDefault(8) + old - count;
            f[3] = count;
            // The server's clock can run ahead of this one; never move the update time backwards.
            f[11] = Math.Max(now, Math.Max(f.GetValueOrDefault(10), f.GetValueOrDefault(11)));
            var buf = new List<byte>();
            foreach (var (no, val) in f.OrderBy(kv => kv.Key))
                if (val != 0) { WriteVarint(buf, (ulong)(no << 3)); WriteVarint(buf, val); }
            itemRows[row.Key] = System.Convert.ToHexString(buf.ToArray()).ToLowerInvariant();
            changed.Add(req.Name);
        }

        var json = account.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
        return new ItemCountResult(Encoding.UTF8.GetBytes(json), changed);
    }

    // Decodes a protobuf message whose fields are all varints (field number -> value).
    private static Dictionary<int, ulong> DecodeVarints(byte[] data)
    {
        var fields = new Dictionary<int, ulong>();
        var i = 0;
        ulong Read()
        {
            ulong v = 0;
            for (var shift = 0; ; shift += 7)
            {
                var b = data[i++];
                v |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return v;
            }
        }
        while (i < data.Length)
        {
            var tag = Read();
            if ((tag & 7) != 0) throw new InvalidDataException("A row in account.json has an unexpected format.");
            fields[(int)(tag >> 3)] = Read();
        }
        return fields;
    }

    private static JsonDocument ParseJson(Stream stream, string what)
    {
        try { return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 256 }); }
        catch (JsonException) { throw new InvalidDataException($"{what} isn't valid JSON."); }
    }

    private static JsonObject ParseNode(Stream stream)
    {
        try
        {
            return JsonNode.Parse(stream) as JsonObject
                   ?? throw new InvalidDataException("The account.json file isn't a JSON object.");
        }
        catch (JsonException) { throw new InvalidDataException("The account.json file isn't valid JSON."); }
    }

    private static long? FirstUserId(JsonElement accountInfo)
    {
        foreach (var t in accountInfo.EnumerateObject())
            if (t.Value.ValueKind == JsonValueKind.Array)
                foreach (var r in t.Value.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("UserId", out var u) && u.TryGetInt64(out var uv))
                        return uv;
        return null;
    }

    // Field order = key order of the first row; fields first seen later are appended.
    private static List<string> Schema(List<JsonElement> rows)
    {
        var order = new List<string>();
        var seen = new HashSet<string>();
        foreach (var row in rows)
            foreach (var p in row.EnumerateObject())
                if (seen.Add(p.Name)) order.Add(p.Name);
        return order;
    }

    // ---- Row keys ("1:<uid>|2:<id>|...") ----

    // A key component, normalised so equal values compare equal (bools count as 0/1, like Python).
    private static string? KeyValue(JsonElement row, List<string> schema, int field, long targetUid)
    {
        if (field < 1 || field > schema.Count) return null;
        var name = schema[field - 1];
        if (name == "UserId") return "L" + targetUid;
        if (!row.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => "L1",
            JsonValueKind.False => "L0",
            JsonValueKind.Number => "L" + v.GetRawText(),
            JsonValueKind.String => "S" + v.GetString(),
            _ => "O" + v.GetRawText(),
        };
    }

    private static bool IsDefaultKey(string? v) => v is null || v == "L0" || v == "S";

    private static bool KeyIsUnique(List<JsonElement> rows, List<string> schema, List<int> fields, long uid)
    {
        var seen = new HashSet<string>();
        foreach (var row in rows)
            if (!seen.Add(string.Join("\u0001", fields.Select(f => KeyValue(row, schema, f, uid) ?? "\u0000"))))
                return false;
        return true;
    }

    private static bool LearnedKeyUsable(List<JsonElement> rows, List<string> schema, List<int> fields, long uid)
    {
        if (fields.Count == 0 || fields.Any(f => f < 1 || f > schema.Count)) return false;
        var seen = new HashSet<string>();
        foreach (var row in rows)
        {
            var parts = new List<string>();
            foreach (var f in fields)
            {
                if (f == 1 && schema[0] == "UserId") { parts.Add($"1:L{uid}"); continue; }
                var v = KeyValue(row, schema, f, uid);
                if (IsDefaultKey(v)) continue;
                if (v![0] == 'O') return false;
                parts.Add($"{f}:{v}");
            }
            if (parts.Count == 0 || !seen.Add(string.Join("|", parts))) return false;
        }
        return true;
    }

    private static List<int> KeyFields(string table, List<JsonElement> rows, List<string> schema, JsonObject? freshRows, bool freshWide, long uid)
    {
        if (SingletonTables.Contains(table)) return new() { 1 };

        // Prefer the key shape the server already uses for this table, if it fits the export.
        var learned = new SortedSet<int>();
        if (freshRows is not null)
            foreach (var kv in freshRows)
                foreach (var part in kv.Key.Split('|'))
                {
                    var bits = part.Split(':', 2);
                    if (bits.Length == 2 && int.TryParse(bits[0], out var f) && long.TryParse(bits[1], out _)) learned.Add(f);
                }
        if (learned.Count > 0 && LearnedKeyUsable(rows, schema, learned.ToList(), uid)) return learned.ToList();

        if (schema.Count <= 1) return new() { 1 };

        // Most tables are keyed on fields 1+2; extend the prefix until rows are unique.
        var fields = new List<int> { 1, 2 };
        while (!KeyIsUnique(rows, schema, fields, uid) && fields.Count < schema.Count)
            fields.Add(fields.Count + 1);
        if (freshWide && fields.Count < 3 && schema.Count >= 3) fields = new() { 1, 2, 3 };
        return fields;
    }

    private static string MakeKey(JsonElement row, List<string> schema, List<int> fields, long uid)
    {
        var parts = new List<string>();
        foreach (var f in fields)
        {
            var isUserId = f == 1 && schema.Count > 0 && schema[0] == "UserId";
            var v = KeyValue(row, schema, f, uid);
            if (!isUserId && IsDefaultKey(v)) continue;
            if (v is null) throw new InvalidDataException($"key field {f} is missing");
            if (v[0] == 'O') throw new InvalidDataException($"key field {f} isn't a plain value");
            parts.Add($"{f}:{v[1..]}");
        }
        if (parts.Count == 0) throw new InvalidDataException("a row produced an empty key");
        return string.Join("|", parts);
    }

    // ---- Protobuf encoding (field number = position in the JSON field order) ----

    private static byte[] EncodeMessage(JsonElement obj, IReadOnlyList<string> order, long? userIdOverride)
    {
        var buf = new List<byte>();
        for (int i = 0; i < order.Count; i++)
        {
            if (!obj.TryGetProperty(order[i], out var v)) continue;
            if (userIdOverride is long uid && order[i] == "UserId")
            {
                if (uid != 0) { WriteVarint(buf, (ulong)((i + 1) << 3)); WriteVarint(buf, (ulong)uid); }
                continue;
            }
            EncodeField(buf, i + 1, v);
        }
        return buf.ToArray();
    }

    private static void EncodeField(List<byte> buf, int field, JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.False:
                return;
            case JsonValueKind.True:
                WriteVarint(buf, (ulong)(field << 3));
                WriteVarint(buf, 1);
                return;
            case JsonValueKind.Number:
                ulong n;
                if (v.TryGetInt64(out var l)) n = (ulong)l;
                else if (v.TryGetUInt64(out var ul)) n = ul;
                else throw new InvalidDataException($"a non-integer number ({v.GetRawText()}) can't be converted");
                if (n == 0) return;
                WriteVarint(buf, (ulong)(field << 3));
                WriteVarint(buf, n);
                return;
            case JsonValueKind.String:
                var s = v.GetString()!;
                if (s.Length == 0) return;
                WriteBytes(buf, field, Encoding.UTF8.GetBytes(s));
                return;
            case JsonValueKind.Object:
                var nested = EncodeMessage(v, v.EnumerateObject().Select(p => p.Name).ToList(), null);
                if (nested.Length > 0) WriteBytes(buf, field, nested);
                return;
            case JsonValueKind.Array:
                foreach (var item in v.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array)
                        throw new InvalidDataException("nested lists can't be converted");
                    EncodeField(buf, field, item);
                }
                return;
        }
    }

    private static void WriteBytes(List<byte> buf, int field, byte[] data)
    {
        WriteVarint(buf, (ulong)((field << 3) | 2));
        WriteVarint(buf, (ulong)data.Length);
        buf.AddRange(data);
    }

    private static void WriteVarint(List<byte> buf, ulong value)
    {
        while (value >= 0x80)
        {
            buf.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        buf.Add((byte)value);
    }
}
