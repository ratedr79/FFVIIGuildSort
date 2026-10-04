using System.Text.Json;
using System.Text.RegularExpressions;

namespace FFVIIEverCrisisAnalyzer.Services;

/// <summary>
/// Finds content that exists in the game's master data but was never released: empty tables,
/// elements/sigils nobody uses, and skipped outfit/weapon numbers. Computed once from the
/// UnknownX7 data so the page stays current when the data is updated.
/// </summary>
public sealed class UnusedContentService
{
    private readonly Lazy<UnusedContentReport> _report;

    public UnusedContentService(IWebHostEnvironment environment, ILogger<UnusedContentService> logger)
    {
        var basePath = Path.Combine(environment.ContentRootPath, "external", "UnknownX7", "FF7EC-Data");
        _report = new Lazy<UnusedContentReport>(() =>
        {
            try { return Build(basePath); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to build unused content report");
                return new UnusedContentReport { Error = "The game data could not be read." };
            }
        });
    }

    public UnusedContentReport Report => _report.Value;

    private static UnusedContentReport Build(string basePath)
    {
        var master = Path.Combine(basePath, "MasterData", "gl");
        var loc = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(basePath, "Localization", "en.json"))) ?? new();
        string? T(long id) => loc.TryGetValue(id.ToString(), out var s) ? s : null;

        var tableSizes = new Dictionary<string, int>();
        foreach (var file in Directory.GetFiles(master, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            tableSizes[Path.GetFileNameWithoutExtension(file)] = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        List<JsonElement> Rows(string table)
        {
            var path = Path.Combine(master, table + ".json");
            if (!File.Exists(path)) return new();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        static long L(JsonElement e, string f) => e.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

        var r = new UnusedContentReport { TableCount = tableSizes.Count };
        r.EmptyTables = tableSizes.Where(kv => kv.Value == 0).Select(kv => kv.Key).OrderBy(x => x).ToList();
        r.TableSizes = tableSizes;

        // Light (8) and Dark (9) elements.
        var resist = Rows("ResistElement");
        r.LightDarkResistRows = resist.Count(x => L(x, "ElementType") is 8 or 9);
        r.LightDarkResistNonZero = resist.Count(x => L(x, "ElementType") is 8 or 9 && L(x, "DamageCoefficient") != 0);
        r.LightDarkSkillRows = Rows("SkillDamageEffect").Count(x => L(x, "ElementType") is 8 or 9)
                             + Rows("SkillBase").Count(x => L(x, "ElementType") is 8 or 9);
        r.DamageChallengeElements = Rows("DamageChallengeBattle").Select(x => L(x, "ElementType")).Distinct().Count();

        // Sigils: SkillNotes type -> definitions and battle pattern uses.
        var notes = Rows("SkillNotes");
        var noteType = notes.ToDictionary(x => L(x, "Id"), x => L(x, "SkillNotesType"));
        var setTypes = new Dictionary<long, HashSet<long>>();
        foreach (var s in Rows("SkillNotesSet"))
            if (noteType.TryGetValue(L(s, "SkillNotesId"), out var t))
                (setTypes.TryGetValue(L(s, "Id"), out var h) ? h : setTypes[L(s, "Id")] = new()).Add(t);
        var uses = new Dictionary<long, int>();
        foreach (var g in Rows("SkillNotesSetGroup"))
            foreach (var t in setTypes.GetValueOrDefault(L(g, "SkillNotesSetId")) ?? new())
                uses[t] = uses.GetValueOrDefault(t) + 1;
        string[] sigilNames = { "", "Circle", "Triangle", "Cross", "Fourth sigil (likely Diamond)", "Fifth sigil (likely Square)" };
        r.Sigils = noteType.Values.Distinct().OrderBy(x => x).Select(t => new SigilUsage(
            (int)t, t < sigilNames.Length ? sigilNames[t] : $"Type {t}",
            notes.Count(n => L(n, "SkillNotesType") == t), uses.GetValueOrDefault(t))).ToList();

        // Skipped outfit and weapon numbers. IDs are character*1000 + sequence.
        var items = Rows("Item").ToDictionary(x => L(x, "Id"), x => T(L(x, "NameLanguageId")));
        var novelCostumes = Rows("CharacterNovelSet").Select(x => L(x, "CharacterNovelCostumeId")).ToHashSet();
        var costumeIds = Rows("CharacterCostume").Select(x => L(x, "Id")).ToHashSet();
        var weaponIds = Rows("Weapon").Select(x => L(x, "Id")).ToHashSet();
        foreach (var c in Rows("Character").OrderBy(x => L(x, "OrderNo")))
        {
            var cid = L(c, "Id");
            var name = Regex.Replace(T(L(c, "NameLanguageId")) ?? $"Character {cid}", "<[^>]+>", "").Trim();
            var row = new CharacterGaps(name);
            foreach (var (ids, list, isWeapon) in new[] { (costumeIds, row.Outfits, false), (weaponIds, row.Weapons, true) })
            {
                var mine = ids.Where(i => i / 1000 == cid).Select(i => i % 1000).ToList();
                if (mine.Count == 0) continue;
                if (isWeapon) { row.WeaponCount = mine.Count; row.WeaponMax = (int)mine.Max(); }
                else { row.OutfitCount = mine.Count; row.OutfitMax = (int)mine.Max(); }
                for (var n = 1; n < mine.Max(); n++)
                {
                    if (mine.Contains(n)) continue;
                    var id = cid * 1000 + n;
                    string? note = null;
                    // A weapon's parts item is 1,000,000 + weapon id; one with no weapon is a strong sign it was cut.
                    if (isWeapon && items.TryGetValue(1_000_000 + id, out var parts) && parts?.EndsWith(" Parts") == true)
                        note = $"“{parts}” item exists";
                    if (!isWeapon && novelCostumes.Contains(id))
                        note = "Referenced by a character story outfit set";
                    list.Add(new GapEntry(id, n, note));
                }
            }
            r.Characters.Add(row);
        }

        // Summon ID gaps.
        var summons = Rows("Summon").Where(x => L(x, "Id") < 50).Select(x => L(x, "Id")).ToList();
        if (summons.Count > 0)
            r.SummonGaps = Enumerable.Range(1, (int)summons.Max()).Select(i => (long)i).Where(i => !summons.Contains(i)).ToList();
        r.Summons = Rows("Summon").OrderBy(x => L(x, "Id")).Select(x => $"{L(x, "Id")} {T(L(x, "NameLanguageId"))}").ToList();

        // Text that is written but whose system is unused.
        r.AccessoryStrings = loc.Where(kv => kv.Value.Length < 100 && Regex.IsMatch(kv.Value, "accessor", RegexOptions.IgnoreCase)).Select(kv => kv.Value).Distinct().ToList();
        r.LightDarkStrings = loc.Count(kv => kv.Value.Length < 100 && Regex.IsMatch(kv.Value, @"\b(Light|Dark)\b (Resist|Pot|Damage|Dmg|dmg|Weakness)"));
        return r;
    }
}

public sealed class UnusedContentReport
{
    public string? Error { get; set; }
    public int TableCount { get; set; }
    public List<string> EmptyTables { get; set; } = new();
    public Dictionary<string, int> TableSizes { get; set; } = new();
    public int LightDarkResistRows { get; set; }
    public int LightDarkResistNonZero { get; set; }
    public int LightDarkSkillRows { get; set; }
    public int DamageChallengeElements { get; set; }
    public int LightDarkStrings { get; set; }
    public List<string> AccessoryStrings { get; set; } = new();
    public List<SigilUsage> Sigils { get; set; } = new();
    public List<CharacterGaps> Characters { get; } = new();
    public List<long> SummonGaps { get; set; } = new();
    public List<string> Summons { get; set; } = new();
}

public sealed record SigilUsage(int Type, string Name, int Definitions, int BattleUses);
public sealed record GapEntry(long Id, long Number, string? Note);

public sealed class CharacterGaps(string name)
{
    public string Name { get; } = name;
    public int OutfitCount { get; set; }
    public int OutfitMax { get; set; }
    public int WeaponCount { get; set; }
    public int WeaponMax { get; set; }
    public List<GapEntry> Outfits { get; } = new();
    public List<GapEntry> Weapons { get; } = new();
}
