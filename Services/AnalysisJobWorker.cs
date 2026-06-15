using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FFVIIEverCrisisAnalyzer.Services
{
    // Drains the AnalysisJobService queue and runs each job in its OWN DI scope (so jobs can resolve the scoped
    // analyzer services safely after the originating request has ended). Bounded concurrency protects the single
    // box: heavy whole-player-base runs and player Full/Pro runs queue instead of all running at once.
    public sealed class AnalysisJobWorker : BackgroundService
    {
        // Max heavy analyses running at once. The whole-player-base job is very CPU-heavy; keep this small.
        private const int MaxConcurrentJobs = 2;

        private readonly AnalysisJobService _jobs;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AnalysisJobWorker> _logger;
        private readonly SemaphoreSlim _gate = new(MaxConcurrentJobs, MaxConcurrentJobs);

        public AnalysisJobWorker(AnalysisJobService jobs, IServiceScopeFactory scopeFactory, ILogger<AnalysisJobWorker> logger)
        {
            _jobs = jobs;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                // Back-pressure: don't dispatch more than MaxConcurrentJobs at a time. Excess jobs stay in the
                // channel (status "Queued") until a slot frees.
                await _gate.WaitAsync(stoppingToken).ConfigureAwait(false);

                _ = Task.Run(() =>
                {
                    try
                    {
                        RunJob(job, stoppingToken);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }, stoppingToken);
            }
        }

        private void RunJob(AnalysisJob job, CancellationToken stoppingToken)
        {
            job.Status = AnalysisJobStatus.Running;
            job.StartedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                job.Result = job.Work(scope.ServiceProvider, stoppingToken);
                job.Status = AnalysisJobStatus.Completed;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                job.Status = AnalysisJobStatus.Failed;
                _logger.LogError(ex, "Analysis job {JobId} failed", job.Id);
            }
            finally
            {
                job.FinishedAtUtc = DateTimeOffset.UtcNow;
            }
        }
    }
}
