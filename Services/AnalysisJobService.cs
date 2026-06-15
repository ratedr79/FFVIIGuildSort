using System.Collections.Concurrent;
using System.Threading.Channels;

namespace FFVIIEverCrisisAnalyzer.Services
{
    // Generic in-process async job runner. Lets long analyses (Player Power Analyzer V2 in Full/Pro, and the
    // whole-player-base Power Level Analyzer) run OUTSIDE the HTTP request, so every request the browser makes
    // (start / status / result) is sub-second and never trips Cloudflare's 100s origin-response timeout (524).
    //
    // Scope: single app instance, in-memory. Jobs are LOST on app restart/deploy (acceptable — re-run). If the
    // app ever scales to multiple instances, this needs a shared store (Redis/DB) or sticky routing.
    public enum AnalysisJobStatus
    {
        Queued,
        Running,
        Completed,
        Failed
    }

    public sealed class AnalysisJob
    {
        public required string Id { get; init; }
        public AnalysisJobStatus Status { get; set; } = AnalysisJobStatus.Queued;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? FinishedAtUtc { get; set; }
        public object? Result { get; set; }
        public string? Error { get; set; }

        // The work resolves its OWN scoped services from the supplied provider — the page's request scope is
        // gone by the time this runs, so the delegate must only capture plain data (inventory json, request DTO).
        public required Func<IServiceProvider, CancellationToken, object> Work { get; init; }

        public int ElapsedMs
        {
            get
            {
                var start = StartedAtUtc ?? CreatedAtUtc;
                var end = FinishedAtUtc ?? DateTimeOffset.UtcNow;
                var ms = (end - start).TotalMilliseconds;
                return ms > 0 ? (int)ms : 0;
            }
        }
    }

    public sealed class AnalysisJobService
    {
        // Keep finished jobs around briefly so the client can fetch the result after the last poll, then evict.
        private static readonly TimeSpan FinishedRetention = TimeSpan.FromMinutes(30);
        private const int MaxRetainedJobs = 200;

        private readonly ConcurrentDictionary<string, AnalysisJob> _jobs = new(StringComparer.Ordinal);
        private readonly Channel<AnalysisJob> _queue = Channel.CreateUnbounded<AnalysisJob>();

        public ChannelReader<AnalysisJob> Reader => _queue.Reader;

        public AnalysisJob Enqueue(Func<IServiceProvider, CancellationToken, object> work)
        {
            EvictStale();

            var job = new AnalysisJob
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Work = work
            };

            _jobs[job.Id] = job;
            _queue.Writer.TryWrite(job);
            return job;
        }

        public AnalysisJob? Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return _jobs.TryGetValue(id, out var job) ? job : null;
        }

        private void EvictStale()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _jobs)
            {
                if (kvp.Value.FinishedAtUtc is { } finished && now - finished > FinishedRetention)
                {
                    _jobs.TryRemove(kvp.Key, out _);
                }
            }

            // Hard cap as a backstop against a flood of jobs: drop the oldest finished ones first.
            if (_jobs.Count > MaxRetainedJobs)
            {
                foreach (var stale in _jobs.Values
                             .Where(j => j.FinishedAtUtc != null)
                             .OrderBy(j => j.FinishedAtUtc)
                             .Take(_jobs.Count - MaxRetainedJobs)
                             .ToList())
                {
                    _jobs.TryRemove(stale.Id, out _);
                }
            }
        }
    }
}
