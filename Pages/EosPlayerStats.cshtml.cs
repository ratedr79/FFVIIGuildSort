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
