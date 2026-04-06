using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class DispatcherRunResult
    {
        public bool Success { get; set; }
        public DispatcherParsedPlan? ParsedPlan { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public interface IDispatcherRunner
    {
        Task<DispatcherRunResult> RunAsync(string googleSheetUrl, int selectedDay, CancellationToken cancellationToken = default);
    }

    public sealed class DispatcherRunnerService : IDispatcherRunner
    {
        private static readonly SemaphoreSlim RunLock = new(1, 1);

        private readonly IConfiguration _configuration;
        private readonly ILogger<DispatcherRunnerService> _logger;

        public DispatcherRunnerService(IConfiguration configuration, ILogger<DispatcherRunnerService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<DispatcherRunResult> RunAsync(string googleSheetUrl, int selectedDay, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(googleSheetUrl))
            {
                return new DispatcherRunResult { Success = false, Error = "Google Sheet URL is required." };
            }

            List<string> allLines;
            try
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(googleSheetUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new DispatcherRunResult { Success = false, Error = $"Failed to download Google Sheet: {response.StatusCode}" };
                }

                var csv = await response.Content.ReadAsStringAsync(cancellationToken);
                allLines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed downloading guild sheet for Dispatcher run.");
                return new DispatcherRunResult { Success = false, Error = $"Failed to read Google Sheet CSV: {ex.Message}" };
            }

            var tsvLines = ExtractDispatcherInputRows(allLines);
            if (tsvLines.Count == 0)
            {
                return new DispatcherRunResult { Success = false, Error = "No data found in expected B4:Q93 range." };
            }

            var dispatcherPath = _configuration.GetValue<string>("Dispatcher:Path") ?? @"D:\code\SOLDIER\Dispatcher";
            var exePath = Path.Combine(dispatcherPath, "Dispatcher.exe");
            var inputPath = Path.Combine(dispatcherPath, "input.txt");

            if (!Directory.Exists(dispatcherPath))
            {
                return new DispatcherRunResult { Success = false, Error = $"Dispatcher directory not found: {dispatcherPath}" };
            }

            if (!File.Exists(exePath))
            {
                return new DispatcherRunResult { Success = false, Error = $"Dispatcher executable not found: {exePath}" };
            }

            await RunLock.WaitAsync(cancellationToken);
            try
            {
                await File.WriteAllTextAsync(inputPath, string.Join(Environment.NewLine, tsvLines), cancellationToken);

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = dispatcherPath,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        stderr.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();

                var reader = process.StandardOutput;
                var buf = new char[1];
                var inputSent = false;
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

                Task<int>? pendingRead = null;
                while (!process.HasExited)
                {
                    var readTask = pendingRead ?? reader.ReadAsync(buf, 0, 1);
                    pendingRead = null;

                    var completed = await Task.WhenAny(readTask, Task.Delay(1500, timeoutCts.Token));
                    if (completed == readTask)
                    {
                        var charsRead = await readTask;
                        if (charsRead == 0)
                        {
                            break;
                        }

                        stdout.Append(buf[0]);
                        continue;
                    }

                    if (!inputSent)
                    {
                        pendingRead = readTask;

                        var outputSoFar = stdout.ToString();
                        var detectedDay = ExtractDetectedDay(outputSoFar);
                        if (detectedDay != selectedDay && selectedDay is >= 1 and <= 3)
                        {
                            await process.StandardInput.WriteLineAsync(selectedDay.ToString());
                        }
                        else
                        {
                            await process.StandardInput.WriteLineAsync();
                        }

                        process.StandardInput.Close();
                        inputSent = true;
                        continue;
                    }

                    pendingRead = readTask;
                    break;
                }

                var remaining = await reader.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrEmpty(remaining))
                {
                    stdout.Append(remaining);
                }

                if (!inputSent)
                {
                    try
                    {
                        process.StandardInput.Close();
                    }
                    catch
                    {
                        // ignored
                    }
                }

                process.WaitForExit(5000);

                if (stderr.Length > 0)
                {
                    return new DispatcherRunResult { Success = false, Error = stderr.ToString().Trim() };
                }

                var output = stdout.ToString();
                if (string.IsNullOrWhiteSpace(output))
                {
                    return new DispatcherRunResult { Success = false, Error = "Dispatcher produced no output." };
                }

                var parsedPlan = ParseDispatcherOutput(output);
                return new DispatcherRunResult { Success = true, ParsedPlan = parsedPlan };
            }
            catch (OperationCanceledException)
            {
                return new DispatcherRunResult { Success = false, Error = "Dispatcher run timed out." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatcher execution failed.");
                return new DispatcherRunResult { Success = false, Error = $"Dispatcher execution failed: {ex.Message}" };
            }
            finally
            {
                RunLock.Release();
            }
        }

        private static List<string> ExtractDispatcherInputRows(List<string> allLines)
        {
            const int startRow = 3;
            const int endRow = 92;
            const int startCol = 1;
            const int endCol = 16;

            var lines = new List<string>();
            for (var r = startRow; r <= endRow && r < allLines.Count; r++)
            {
                var fields = ParseCsvLine(allLines[r]);
                var selectedFields = new List<string>();
                for (var c = startCol; c <= endCol; c++)
                {
                    selectedFields.Add(c < fields.Count ? fields[c] : string.Empty);
                }

                lines.Add(string.Join("\t", selectedFields));
            }

            return lines;
        }

        private static int ExtractDetectedDay(string output)
        {
            var match = Regex.Match(output, @"day\s+(\d)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return 0;
            }

            return int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : 0;
        }

        private static DispatcherParsedPlan ParseDispatcherOutput(string output)
        {
            var plan = new DispatcherParsedPlan { RawOutput = output };
            foreach (var stage in Enum.GetValues<StageId>())
            {
                plan.StageAssignments[stage] = new List<DispatcherPlayerAssignment>();
            }

            var resetMatch = Regex.Match(output, @"anticipating\s+(\d+)\s+reset", RegexOptions.IgnoreCase);
            if (resetMatch.Success && int.TryParse(resetMatch.Groups[1].Value, out var resets))
            {
                plan.ExpectedResets = resets;
            }

            var lines = output.Split('\n').Select(l => l.Trim()).ToArray();
            StageId? currentStage = null;

            foreach (var line in lines)
            {
                var stageMatch = Regex.Match(line, @"---\s*Stage\s+(\d+)\s*---");
                if (stageMatch.Success && int.TryParse(stageMatch.Groups[1].Value, out var stageNum) && stageNum >= 1 && stageNum <= 6)
                {
                    currentStage = (StageId)stageNum;
                    continue;
                }

                if (!currentStage.HasValue || !line.Contains('@'))
                {
                    continue;
                }

                foreach (var entry in line.Split(','))
                {
                    var playerMatch = Regex.Match(entry.Trim(), @"@([\w\d]+)(?:\s*\((\d+)\))?");
                    if (!playerMatch.Success)
                    {
                        continue;
                    }

                    var attacks = 0;
                    if (playerMatch.Groups[2].Success)
                    {
                        int.TryParse(playerMatch.Groups[2].Value, out attacks);
                    }

                    plan.StageAssignments[currentStage.Value].Add(new DispatcherPlayerAssignment
                    {
                        PlayerName = playerMatch.Groups[1].Value,
                        Attacks = attacks
                    });
                }
            }

            return plan;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var inQuotes = false;
            var current = new StringBuilder();

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
