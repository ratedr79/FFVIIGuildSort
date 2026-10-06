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
            if ((tag & 7) != 0) throw new InvalidDataException("An Escalation Challenge row in account.json has an unexpected format.");
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
