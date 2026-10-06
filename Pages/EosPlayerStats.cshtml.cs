using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;
using FFVIIEverCrisisAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FFVIIEverCrisisAnalyzer.Pages;

// Exports are ~7 MB today; leave generous headroom as accounts grow.
[RequestSizeLimit(64 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 64 * 1024 * 1024)]
public class EosPlayerStatsModel : PageModel
{
    private readonly EosPlayerStatsService _service;
    private readonly ILogger<EosPlayerStatsModel> _logger;

    public EosPlayerStatsModel(EosPlayerStatsService service, ILogger<EosPlayerStatsModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    [BindProperty]
    public bool IncludePurchases { get; set; }

    public EosPlayerStats? Stats { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? FileName { get; private set; }

    public void OnGet()
    {
    }

    public void OnPost()
    {
        if (UploadedFile is null || UploadedFile.Length == 0)
        {
            ErrorMessage = "Choose your EOS export (.json) file first.";
            return;
        }

        FileName = UploadedFile.FileName;
        try
        {
            // Parsed in memory only; the upload is never written to disk.
            using var stream = UploadedFile.OpenReadStream();
            Stats = _service.Parse(stream, IncludePurchases);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            ErrorMessage = ex is InvalidDataException
                ? ex.Message
                : "That file isn't valid JSON. Upload the .json file exported from EOS as-is.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse EOS export {File}", UploadedFile.FileName);
            ErrorMessage = "Something went wrong reading that file.";
        }
    }

    [BindProperty]
    public IFormFile? ConvertSourceFile { get; set; }

    [BindProperty]
    public IFormFile? ServerAccountFile { get; set; }

    public string? ConvertErrorMessage { get; private set; }

    // Builds a NeverCrisis offline-server account.json from an EOS export plus the
    // account.json the server generated. Both files are read in memory only.
    public IActionResult OnPostNeverCrisis()
    {
        if (ConvertSourceFile is null || ConvertSourceFile.Length == 0)
        {
            ConvertErrorMessage = "Choose your EOS export (.json) file.";
            return Page();
        }
        if (ServerAccountFile is null || ServerAccountFile.Length == 0)
        {
            ConvertErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }

        try
        {
            using var source = ConvertSourceFile.OpenReadStream();
            using var template = ServerAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.Convert(source, template);
            _logger.LogInformation("NeverCrisis export: {Tables} tables, {Rows} rows, {Stones} stone types",
                result.Tables, result.Rows, result.StoneTypes);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            ConvertErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NeverCrisis conversion failed for {File}", ConvertSourceFile.FileName);
            ConvertErrorMessage = "Something went wrong building the account file.";
        }
        return Page();
    }

    public IReadOnlyList<GearOption> Gear => _service.GearCatalog();

    [BindProperty]
    public IFormFile? GearAccountFile { get; set; }

    // Selected gear as "o:<costumeId>" / "w:<weaponId>".
    [BindProperty]
    public List<string> AddGearIds { get; set; } = new();

    public string? GearErrorMessage { get; private set; }

    public IActionResult OnPostAddGear()
    {
        if (GearAccountFile is null || GearAccountFile.Length == 0)
        {
            GearErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        var lookup = Gear.ToDictionary(g => (g.Type == GearType.Outfit ? "o:" : "w:") + g.Id);
        var selected = AddGearIds.Select(id => lookup.GetValueOrDefault(id)).OfType<GearOption>().ToList();
        if (selected.Count == 0)
        {
            GearErrorMessage = "Tick at least one outfit or weapon to add.";
            return Page();
        }

        try
        {
            using var account = GearAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.AddGear(account, selected);
            if (result.Added.Count == 0)
            {
                GearErrorMessage = "Everything you picked is already in that account: "
                    + string.Join(", ", result.AlreadyOwned.Select(g => g.Name)) + ". No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis gear: added {Added}, already owned {Owned}", result.Added.Count, result.AlreadyOwned.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            GearErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adding gear to a NeverCrisis account failed");
            GearErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<HighwindItemOption> HighwindItems => _service.HighwindCatalog();

    [BindProperty]
    public IFormFile? HighwindAccountFile { get; set; }

    // Collection item ids ticked as owned.
    [BindProperty]
    public List<long> HighwindOwn { get; set; } = new();

    // Item id -> overboost count; blank = unchanged.
    public Dictionary<long, int?> HighwindUpgrade { get; private set; } = new();

    // "<item id>_<bonus type>" -> steps received; blank = unchanged.
    public Dictionary<string, int?> HighwindBonus { get; private set; } = new();

    public string? HighwindErrorMessage { get; private set; }

    public IActionResult OnPostSetHighwind()
    {
        HighwindUpgrade = FormNumbers("HighwindUpgrade").Where(kv => long.TryParse(kv.Key, out _)).ToDictionary(kv => long.Parse(kv.Key), kv => kv.Value);
        HighwindBonus = FormNumbers("HighwindBonus");
        if (HighwindAccountFile is null || HighwindAccountFile.Length == 0)
        {
            HighwindErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }

        var requests = new List<NeverCrisisAccountConverter.HighwindRequest>();
        foreach (var item in HighwindItems)
        {
            int? upgrade = HighwindUpgrade.GetValueOrDefault(item.Id);
            if (upgrade is < 0 || upgrade > item.MaxUpgrade)
            {
                HighwindErrorMessage = $"{item.Name} overboosts from 0 to {item.MaxUpgrade}.";
                return Page();
            }
            var steps = new Dictionary<int, int>();
            foreach (var bonus in item.Bonuses)
            {
                if (HighwindBonus.GetValueOrDefault($"{item.Id}_{bonus.Type}") is not int count) continue;
                if (count < 0 || count > bonus.Indices.Count)
                {
                    HighwindErrorMessage = $"{item.Name}: {bonus.Name} has {bonus.Indices.Count} steps; enter 0 to {bonus.Indices.Count}.";
                    return Page();
                }
                steps[bonus.Type] = count;
            }
            requests.Add(new(item, HighwindOwn.Contains(item.Id), upgrade, steps));
        }

        try
        {
            using var account = HighwindAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetHighwind(account, requests);
            if (result.Changed.Count == 0)
            {
                HighwindErrorMessage = "Nothing to change: the account already matches what you entered. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis Highwind: updated {Count} collection items", result.Changed.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            HighwindErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting Highwind items in a NeverCrisis account failed");
            HighwindErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<MemoriaOption> MemoriaOptions => _service.MemoriaCatalog();

    [BindProperty]
    public IFormFile? MemoriaAccountFile { get; set; }

    // Memoria id -> level to own it at; blank = unchanged.
    public Dictionary<long, int?> MemoriaLevels { get; private set; } = new();

    public string? MemoriaErrorMessage { get; private set; }

    public IActionResult OnPostSetMemoria()
    {
        MemoriaLevels = FormNumbers("MemoriaLevels").Where(kv => long.TryParse(kv.Key, out _)).ToDictionary(kv => long.Parse(kv.Key), kv => kv.Value);
        if (MemoriaAccountFile is null || MemoriaAccountFile.Length == 0)
        {
            MemoriaErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        var lookup = MemoriaOptions.ToDictionary(m => m.Id);
        var selected = new List<(MemoriaOption, int)>();
        foreach (var (id, level) in MemoriaLevels)
        {
            if (level is null || !lookup.TryGetValue(id, out var memoria)) continue;
            if (level < 1 || level > memoria.MaxLevel)
            {
                MemoriaErrorMessage = memoria.MaxLevel == 1
                    ? $"{memoria.Name} only has level 1."
                    : $"{memoria.Name} goes from level 1 to {memoria.MaxLevel}.";
                return Page();
            }
            selected.Add((memoria, level.Value));
        }
        if (selected.Count == 0)
        {
            MemoriaErrorMessage = "Enter a level for at least one memoria.";
            return Page();
        }

        try
        {
            using var account = MemoriaAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetMemoria(account, selected);
            if (result.Changed.Count == 0)
            {
                MemoriaErrorMessage = "That account already has those memoria at those levels. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis memoria: updated {Count} memoria", result.Changed.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            MemoriaErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting memoria in a NeverCrisis account failed");
            MemoriaErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<MateriaAddOption> MateriaAddOptions => _service.MateriaAddCatalog();

    [BindProperty]
    public IFormFile? MateriaAccountFile { get; set; }

    // The materia list built in the page, as JSON: [{ key, level, notes, stats: [{ id, value, boosts }] }].
    [BindProperty]
    public string? MateriaRequestsJson { get; set; }

    // Lets each stat use every boost the level gives, so all four can sit at their top (not possible in game).
    [BindProperty]
    public bool MateriaIgnoreBoostCap { get; set; }

    public string? MateriaErrorMessage { get; private set; }

    private sealed record MateriaRequestDto(string? Key, int Level, int Notes, List<MateriaStatDto>? Stats);
    private sealed record MateriaStatDto(long Id, int Value, int Boosts);

    public IActionResult OnPostSetMateria()
    {
        if (MateriaAccountFile is null || MateriaAccountFile.Length == 0)
        {
            MateriaErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        List<MateriaRequestDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<MateriaRequestDto>>(MateriaRequestsJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            dtos = null;
        }
        if (dtos is null || dtos.Count == 0)
        {
            MateriaErrorMessage = "Add at least one materia to the list.";
            return Page();
        }

        var lookup = MateriaAddOptions.ToDictionary(m => m.Key);
        var requests = new List<MateriaAddRequest>();
        foreach (var d in dtos)
        {
            if (d.Key is null || !lookup.TryGetValue(d.Key, out var m))
            {
                MateriaErrorMessage = "One of the materia in the list isn't recognized. Pick it again from the list.";
                return Page();
            }
            if (d.Level < 1 || d.Level > m.MaxLevel)
            {
                MateriaErrorMessage = m.MaxLevel == 1 ? $"{m.Name} only has level 1." : $"{m.Name} goes from level 1 to {m.MaxLevel}.";
                return Page();
            }
            if (!m.Sigils.Contains(d.Notes))
            {
                MateriaErrorMessage = $"{m.Name} can't have that sigil.";
                return Page();
            }
            var stats = d.Stats ?? new();
            if (stats.Count is < 1 or > 4 || stats.Select(s => s.Id).Distinct().Count() != stats.Count)
            {
                MateriaErrorMessage = $"{m.Name} needs one to four different stats.";
                return Page();
            }
            var pool = m.Stats.ToDictionary(s => s.Id);
            if (m.Refined && (stats.Count != m.Stats.Count || stats.Any(s => !pool.ContainsKey(s.Id))))
            {
                MateriaErrorMessage = $"{m.Name} always has its four fixed stats.";
                return Page();
            }
            var boostsAvailable = m.BoostsAt(d.Level);
            if (stats.Any(s => s.Boosts > boostsAvailable) || (!MateriaIgnoreBoostCap && stats.Sum(s => s.Boosts) > boostsAvailable))
            {
                MateriaErrorMessage = $"{m.Name} at level {d.Level} has {boostsAvailable} stat boost{(boostsAvailable == 1 ? "" : "s")}, but the stats use {stats.Sum(s => s.Boosts)}.";
                return Page();
            }
            foreach (var s in stats)
            {
                if (!pool.TryGetValue(s.Id, out var opt))
                {
                    MateriaErrorMessage = $"{m.Name} can't roll that stat.";
                    return Page();
                }
                if (s.Boosts < 0 || s.Value < opt.Min(s.Boosts) || s.Value > opt.Max(s.Boosts))
                {
                    string Fmt(int v) => opt.Percent ? (v / 10.0).ToString("0.#") + "%" : v.ToString();
                    MateriaErrorMessage = $"{m.Name}: {opt.Label} boosted {s.Boosts} time{(s.Boosts == 1 ? "" : "s")} must be {Fmt(opt.Min(s.Boosts))} to {Fmt(opt.Max(s.Boosts))}.";
                    return Page();
                }
            }
            requests.Add(new MateriaAddRequest(m, d.Level, d.Notes, stats.Select(s => (s.Id, s.Value, s.Boosts)).ToList()));
        }
        if (requests.Count > 200)
        {
            MateriaErrorMessage = "Add at most 200 materia at a time.";
            return Page();
        }

        try
        {
            using var account = MateriaAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetMateria(account, requests);
            _logger.LogInformation("NeverCrisis materia: added {Count} materia", result.Changed.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            MateriaErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adding materia to a NeverCrisis account failed");
            MateriaErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<BrandStoneOption> BrandStones => _service.BrandStoneCatalog();

    // Weapon id -> [name, character], for listing an account's weapons in the brand editor.
    public Dictionary<long, string[]> BrandWeaponNames => _service.GearCatalog().Where(g => g.Type == GearType.Weapon)
        .ToDictionary(g => g.Id, g => new[] { g.Name, g.Character });

    [BindProperty]
    public IFormFile? BrandAccountFile { get; set; }

    // The weapons to brand, built in the page as JSON: [{ weaponId, stone, lines: [{ type, value }] }].
    [BindProperty]
    public string? BrandRequestsJson { get; set; }

    public string? BrandErrorMessage { get; private set; }

    private sealed record BrandRequestDto(long WeaponId, long Stone, List<BrandLineDto>? Lines);
    private sealed record BrandLineDto(int Type, int Value);

    public IActionResult OnPostSetBrands()
    {
        if (BrandAccountFile is null || BrandAccountFile.Length == 0)
        {
            BrandErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        List<BrandRequestDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<BrandRequestDto>>(BrandRequestsJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            dtos = null;
        }
        if (dtos is null || dtos.Count == 0)
        {
            BrandErrorMessage = "Add at least one weapon to the list.";
            return Page();
        }

        var stones = BrandStones.ToDictionary(b => b.ItemId);
        var weaponNames = _service.GearCatalog().Where(g => g.Type == GearType.Weapon).ToDictionary(g => g.Id, g => g.Name);
        var requests = new List<WeaponBrandRequest>();
        foreach (var d in dtos.DistinctBy(d => d.WeaponId))
        {
            var name = weaponNames.GetValueOrDefault(d.WeaponId) ?? $"weapon #{d.WeaponId}";
            var lines = d.Lines ?? new();
            if (lines.Count == 0)
            {
                requests.Add(new WeaponBrandRequest(d.WeaponId, name, Array.Empty<long>()));
                continue;
            }
            if (!stones.TryGetValue(d.Stone, out var stone))
            {
                BrandErrorMessage = $"{name}: pick a branding stone.";
                return Page();
            }
            if (lines.Count > stone.MaxLines)
            {
                BrandErrorMessage = $"{name}: a weapon has at most {stone.MaxLines} brands.";
                return Page();
            }
            var ids = new List<long>();
            foreach (var line in lines)
            {
                var stat = stone.Stats.FirstOrDefault(x => x.Type == line.Type);
                if (stat is null)
                {
                    BrandErrorMessage = $"{name}: the {stone.Name} can't brand that stat.";
                    return Page();
                }
                string Fmt(int v) => stat.Percent ? (v / 10.0).ToString("0.0") + "%" : v.ToString();
                var id = stone.EffectId(stat, line.Value);
                if (line.Value < stat.Min || line.Value > stat.Max || !_service.IsBrandEffect(id))
                {
                    BrandErrorMessage = $"{name}: {stat.Label} from the {stone.Name} must be {Fmt(stat.Min)} to {Fmt(stat.Max)}.";
                    return Page();
                }
                ids.Add(id);
            }
            requests.Add(new WeaponBrandRequest(d.WeaponId, name, ids));
        }

        try
        {
            using var account = BrandAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetBrands(account, requests);
            if (result.Changed.Count == 0)
            {
                BrandErrorMessage = "Nothing to change: those weapons already have exactly these brands. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis brands: updated {Count} weapons", result.Changed.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            BrandErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Branding weapons in a NeverCrisis account failed");
            BrandErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<PlayerRankOption> PlayerRanks => _service.PlayerRanks();

    [BindProperty]
    public IFormFile? RankAccountFile { get; set; }

    [BindProperty]
    public int? RankTarget { get; set; }

    [BindProperty]
    public bool RankFillStamina { get; set; } = true;

    public string? RankErrorMessage { get; private set; }

    public IActionResult OnPostSetRank()
    {
        if (RankAccountFile is null || RankAccountFile.Length == 0)
        {
            RankErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        var ranks = PlayerRanks;
        var rank = ranks.FirstOrDefault(r => r.Rank == RankTarget);
        if (rank is null)
        {
            RankErrorMessage = $"Pick a rank from 1 to {(ranks.Count > 0 ? ranks[^1].Rank : 0)}.";
            return Page();
        }
        try
        {
            using var account = RankAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetPlayerRank(account, rank.RequiredExp, RankFillStamina ? rank.StaminaMax : null);
            if (result.OldExp == result.NewExp && result.OldStamina == result.NewStamina)
            {
                RankErrorMessage = $"Nothing to change: the account is already at the start of rank {rank.Rank}. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis rank: set to {Rank}", rank.Rank);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            RankErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting the player rank in a NeverCrisis account failed");
            RankErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyDictionary<long, WeaponProgressOption> WeaponProgress => _service.WeaponProgressCatalog();

    [BindProperty]
    public IFormFile? WeaponLevelAccountFile { get; set; }

    // The weapons to raise, built in the page as JSON: [{ weaponId, rarity, release, level, overboost }].
    [BindProperty]
    public string? WeaponLevelRequestsJson { get; set; }

    public string? WeaponLevelErrorMessage { get; private set; }

    private sealed record WeaponLevelDto(long WeaponId, int Rarity, int Release, int Level, int Overboost);

    private static string RarityLabel(int rarity) => rarity switch { 1 => "3★", 2 => "4★", 3 => "5★", 101 => "6★ (ultimate)", _ => $"rarity {rarity}" };

    public IActionResult OnPostSetWeaponLevels()
    {
        if (WeaponLevelAccountFile is null || WeaponLevelAccountFile.Length == 0)
        {
            WeaponLevelErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        List<WeaponLevelDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<WeaponLevelDto>>(WeaponLevelRequestsJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            dtos = null;
        }
        if (dtos is null || dtos.Count == 0)
        {
            WeaponLevelErrorMessage = "Add at least one weapon to the list.";
            return Page();
        }

        var progress = WeaponProgress;
        var weaponNames = _service.GearCatalog().Where(g => g.Type == GearType.Weapon).ToDictionary(g => g.Id, g => g.Name);
        var requests = new List<WeaponProgressRequest>();
        foreach (var d in dtos.DistinctBy(d => d.WeaponId))
        {
            var name = weaponNames.GetValueOrDefault(d.WeaponId) ?? $"weapon #{d.WeaponId}";
            if (!progress.TryGetValue(d.WeaponId, out var p))
            {
                WeaponLevelErrorMessage = $"{name}: no level data for this weapon.";
                return Page();
            }
            if (!p.MaxRelease.TryGetValue(d.Rarity, out var maxRelease))
            {
                WeaponLevelErrorMessage = $"{name} can't be {RarityLabel(d.Rarity)}; it can be {string.Join(" or ", p.MaxRelease.Keys.Select(RarityLabel))}.";
                return Page();
            }
            if (d.Release < 0 || d.Release > maxRelease)
            {
                WeaponLevelErrorMessage = $"{name}: a {RarityLabel(d.Rarity)} weapon's level cap goes up to {p.MaxLevel(maxRelease)}.";
                return Page();
            }
            var cap = Math.Min(p.MaxLevel(d.Release), p.LevelExp.Count);
            if (d.Level < 1 || d.Level > cap)
            {
                WeaponLevelErrorMessage = $"{name}: with a level cap of {cap} the level is 1 to {cap}.";
                return Page();
            }
            var maxOverboost = d.Rarity == p.MaxRelease.Keys.Max() ? p.Overboost1 + p.Overboost2 : 0;
            if (d.Overboost < 0 || d.Overboost > maxOverboost)
            {
                WeaponLevelErrorMessage = maxOverboost == 0
                    ? $"{name}: overboost needs the weapon at its top rarity."
                    : $"{name}: overboost is 0 to {maxOverboost}.";
                return Page();
            }
            var (type, count) = d.Overboost <= p.Overboost1 ? (1, d.Overboost) : (2, d.Overboost - p.Overboost1);
            requests.Add(new WeaponProgressRequest(d.WeaponId, name, d.Rarity, d.Release, p.LevelExp[d.Level - 1], type, count));
        }

        try
        {
            using var account = WeaponLevelAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetWeaponProgress(account, requests);
            if (result.Changed.Count == 0)
            {
                WeaponLevelErrorMessage = "Nothing to change: those weapons are already exactly like this. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis weapon levels: updated {Count} weapons", result.Changed.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            WeaponLevelErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting weapon levels in a NeverCrisis account failed");
            WeaponLevelErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<ItemOption> ItemOptions => _service.ItemCatalog();

    [BindProperty]
    public IFormFile? ItemAccountFile { get; set; }

    // JSON list of { itemId, count } from the item editor.
    [BindProperty]
    public string? ItemRequestsJson { get; set; }

    public string? ItemErrorMessage { get; private set; }

    private sealed record ItemCountDto(long ItemId, long Count);

    public IActionResult OnPostSetItems()
    {
        if (ItemAccountFile is null || ItemAccountFile.Length == 0)
        {
            ItemErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        List<ItemCountDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<ItemCountDto>>(ItemRequestsJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { dtos = null; }
        if (dtos is null || dtos.Count == 0)
        {
            ItemErrorMessage = "Add at least one item to the list.";
            return Page();
        }

        var catalog = ItemOptions.ToDictionary(i => i.Id);
        var requests = new List<ItemCountRequest>();
        foreach (var d in dtos)
        {
            if (!catalog.TryGetValue(d.ItemId, out var item))
            {
                ItemErrorMessage = $"Item #{d.ItemId} isn't an item the game knows.";
                return Page();
            }
            if (d.Count < 0 || d.Count > item.MaxCapacity)
            {
                ItemErrorMessage = $"{item.Name}: the count is 0 to {item.MaxCapacity:N0}.";
                return Page();
            }
            requests.Add(new ItemCountRequest(item.Id, item.Name, d.Count));
        }

        try
        {
            using var account = ItemAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetItemCounts(account, requests);
            if (result.Changed.Count == 0)
            {
                ItemErrorMessage = "Nothing to change: the account already has exactly those counts. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis items: updated {Count} items", result.Changed.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            ItemErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting items in a NeverCrisis account failed");
            ItemErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public MateriaListCatalog MateriaList => _service.MateriaListCatalog();

    [BindProperty]
    public IFormFile? MateriaDeleteAccountFile { get; set; }

    // JSON list of UserMateriaIds (as strings; they don't fit in a JavaScript number).
    [BindProperty]
    public string? MateriaDeleteJson { get; set; }

    // Adds the Gil the game would pay for selling the deleted materia.
    [BindProperty]
    public bool MateriaDeleteSell { get; set; }

    public string? MateriaDeleteErrorMessage { get; private set; }

    public IActionResult OnPostDeleteMateria()
    {
        if (MateriaDeleteAccountFile is null || MateriaDeleteAccountFile.Length == 0)
        {
            MateriaDeleteErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        List<string>? raw;
        try { raw = JsonSerializer.Deserialize<List<string>>(MateriaDeleteJson ?? "[]"); }
        catch (JsonException) { raw = null; }
        var ids = new List<ulong>();
        foreach (var s in raw ?? new())
            if (ulong.TryParse(s, out var id) && id > 0) ids.Add(id);
        if (ids.Count == 0)
        {
            MateriaDeleteErrorMessage = "Tick at least one materia to delete.";
            return Page();
        }

        try
        {
            using var account = MateriaDeleteAccountFile.OpenReadStream();
            var catalog = MateriaList;
            var result = NeverCrisisAccountConverter.DeleteMateria(account, ids,
                MateriaDeleteSell ? f => _service.MateriaSaleGil((long)f.GetValueOrDefault(3), (int)f.GetValueOrDefault(4),
                    (int)f.GetValueOrDefault(5), (long)f.GetValueOrDefault(7)) : null,
                catalog.GilItemId, catalog.GilMax);
            if (result.Deleted == 0)
            {
                MateriaDeleteErrorMessage = "Nothing was deleted: " + string.Join("; ", result.Skipped.Take(5))
                    + ". Locked materia and materia equipped in a party can't be deleted. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis materia: deleted {Count}, skipped {Skipped}, Gil +{Gil}",
                result.Deleted, result.Skipped.Count, result.GilAdded);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            MateriaDeleteErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting materia from a NeverCrisis account failed");
            MateriaDeleteErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<ChocoboOption> ChocoboOptions => _service.ChocoboCatalog();

    [BindProperty]
    public IFormFile? ChocoboAccountFile { get; set; }

    [BindProperty]
    public bool ChocoboCompleteTutorials { get; set; } = true;

    // JSON list of { id, speed, intellect, count } from the chocobo adder. Speed and intellect are the Speed and
    // Intellect weights; Stamina and Adaptability make up the rest of each pair. A null weight is rolled like the game.
    [BindProperty]
    public string? ChocoboRequestsJson { get; set; }

    public string? ChocoboErrorMessage { get; private set; }

    private sealed record ChocoboDto(long Id, int? Speed, int? Intellect, int Count);

    public IActionResult OnPostAddChocobos()
    {
        if (ChocoboAccountFile is null || ChocoboAccountFile.Length == 0)
        {
            ChocoboErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        List<ChocoboDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<ChocoboDto>>(ChocoboRequestsJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { dtos = null; }
        // An empty list is fine: the download then only sets up the ranch.
        dtos ??= new();

        var catalog = ChocoboOptions.ToDictionary(c => c.Id);
        var requests = new List<ChocoboAddRequest>();
        foreach (var d in dtos)
        {
            if (!catalog.TryGetValue(d.Id, out var c))
            {
                ChocoboErrorMessage = $"Chocobo #{d.Id} isn't one the game knows.";
                return Page();
            }
            if (d.Count is < 1 or > 50)
            {
                ChocoboErrorMessage = "Add 1 to 50 of each chocobo at a time.";
                return Page();
            }
            for (var n = 0; n < d.Count; n++)
            {
                // Each pair (Speed + Stamina, Intellect + Adaptability) keeps its total; the first of the pair may move by the spread.
                int Pick(int? value, int first, int second)
                {
                    var v = value ?? first + Random.Shared.Next(-c.Spread, c.Spread + 1);
                    return Math.Clamp(v, Math.Max(0, first - c.Spread), Math.Min(first + second, first + c.Spread));
                }
                var speed = Pick(d.Speed, c.Weights[0], c.Weights[1]);
                var intellect = Pick(d.Intellect, c.Weights[2], c.Weights[3]);
                requests.Add(new ChocoboAddRequest(c, new[]
                {
                    speed, c.Weights[0] + c.Weights[1] - speed, intellect, c.Weights[2] + c.Weights[3] - intellect,
                }));
            }
        }
        if (requests.Count > 200)
        {
            ChocoboErrorMessage = "Add at most 200 chocobos at a time.";
            return Page();
        }

        try
        {
            using var account = ChocoboAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.AddChocobos(account, requests);
            if (ChocoboCompleteTutorials)
                result = result with { Json = NeverCrisisAccountConverter.CompleteTutorials(new MemoryStream(result.Json), _service.TutorialLastSteps()).Json };
            _logger.LogInformation("NeverCrisis chocobos: added {Count}, ranch set up {Ranch}", result.Added, result.RanchSetUp);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            ChocoboErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adding chocobos to a NeverCrisis account failed");
            ChocoboErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<StoryChapterOption> StoryChapters => _service.StoryCatalog();

    [BindProperty]
    public IFormFile? StoryAccountFile { get; set; }

    [BindProperty]
    public bool StoryCompleteTutorials { get; set; } = true;

    // JSON list of chapter keys ("m101" main story chapter, "c101" character story section) to mark cleared.
    [BindProperty]
    public string? StoryChaptersJson { get; set; }

    public string? StoryErrorMessage { get; private set; }

    public IActionResult OnPostSetStory()
    {
        if (StoryAccountFile is null || StoryAccountFile.Length == 0)
        {
            StoryErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        List<string>? keys;
        try { keys = JsonSerializer.Deserialize<List<string>>(StoryChaptersJson ?? "[]"); }
        catch (JsonException) { keys = null; }
        var catalog = StoryChapters.ToDictionary(c => c.Key);
        var chapters = (keys ?? new()).Where(catalog.ContainsKey).Distinct().Select(k => catalog[k]).ToList();
        if (chapters.Count == 0)
        {
            StoryErrorMessage = "Tick at least one chapter to mark complete.";
            return Page();
        }

        try
        {
            using var account = StoryAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetStoryProgress(account, chapters);
            if (StoryCompleteTutorials)
                result = result with { Json = NeverCrisisAccountConverter.CompleteTutorials(new MemoryStream(result.Json), _service.TutorialLastSteps()).Json };
            _logger.LogInformation("NeverCrisis story: {Chapters} chapters, {Episodes} new episode clears", result.Chapters, result.Episodes);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            StoryErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting story progress on a NeverCrisis account failed");
            StoryErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    public IReadOnlyList<EscalationOption> Escalations => _service.EscalationCatalog();

    [BindProperty]
    public IFormFile? EscalationAccountFile { get; set; }

    // Challenge id -> highest cleared stage; blank entries are left unchanged.
    public Dictionary<long, int?> EscalationLevels { get; private set; } = new();

    public string? EscalationErrorMessage { get; private set; }

    public IActionResult OnPostSetEscalation()
    {
        EscalationLevels = FormNumbers("EscalationLevels").Where(kv => long.TryParse(kv.Key, out _)).ToDictionary(kv => long.Parse(kv.Key), kv => kv.Value);
        if (EscalationAccountFile is null || EscalationAccountFile.Length == 0)
        {
            EscalationErrorMessage = "Choose the account.json from your NeverCrisis server's data folder.";
            return Page();
        }
        var lookup = Escalations.ToDictionary(e => e.Id);
        var selected = new List<(EscalationOption, int)>();
        foreach (var (id, level) in EscalationLevels)
        {
            if (level is null || !lookup.TryGetValue(id, out var challenge)) continue;
            if (level < 0 || level > challenge.MaxLevel)
            {
                EscalationErrorMessage = $"{challenge.Name} has {challenge.MaxLevel} stages; enter a number from 0 to {challenge.MaxLevel}.";
                return Page();
            }
            selected.Add((challenge, level.Value));
        }
        if (selected.Count == 0)
        {
            EscalationErrorMessage = "Enter the cleared stage for at least one challenge.";
            return Page();
        }

        try
        {
            using var account = EscalationAccountFile.OpenReadStream();
            var result = NeverCrisisAccountConverter.SetEscalation(account, selected);
            if (result.Changed.Count == 0)
            {
                EscalationErrorMessage = "That account already has those stages cleared. No file was built.";
                return Page();
            }
            _logger.LogInformation("NeverCrisis escalation: updated {Count} challenges", result.Changed.Count);
            return File(result.Json, "application/json", "account.json");
        }
        catch (InvalidDataException ex)
        {
            EscalationErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting Escalation clears in a NeverCrisis account failed");
            EscalationErrorMessage = "Something went wrong updating the account file.";
        }
        return Page();
    }

    // Reads "<prefix>[key]" number fields from the posted form. These are read by hand rather than
    // model-bound: a bound dictionary with no matching keys falls back to binding every form field.
    private Dictionary<string, int?> FormNumbers(string prefix)
    {
        var result = new Dictionary<string, int?>();
        foreach (var (key, value) in Request.Form)
        {
            if (!key.StartsWith(prefix + "[", StringComparison.Ordinal) || !key.EndsWith(']')) continue;
            var raw = value.ToString().Trim();
            result[key[(prefix.Length + 1)..^1]] = int.TryParse(raw, out var n) ? n : raw.Length == 0 ? null : -1;
        }
        return result;
    }
}
