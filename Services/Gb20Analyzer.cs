using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class Gb20Analyzer
    {
        private readonly Gb20Ingestion _ingestion;
        private readonly TeamOptimizer _optimizer;

        public Gb20Analyzer(Gb20Ingestion ingestion, TeamOptimizer optimizer)
        {
            _ingestion = ingestion;
            _optimizer = optimizer;
        }

        public async Task<List<BestTeamResult>> AnalyzeAsync(Stream gb20CsvStream)
            => await AnalyzeAsync(gb20CsvStream, new BattleContext());

        public async Task<IngestionResult> ReadAccountsAsync(Stream gb20CsvStream)
            => await _ingestion.ReadAccountsAsync(gb20CsvStream);

        public async Task<List<BestTeamResult>> AnalyzeAsync(IReadOnlyList<AccountRow> accounts, BattleContext context)
        {
            context ??= new BattleContext();

            var results = new List<BestTeamResult>();
            foreach (var account in accounts)
            {
                results.Add(_optimizer.FindBestTeam(account, context));
            }

            return await Task.FromResult(results
                .OrderByDescending(r => r.Score)
                .ToList());
        }

        public async Task<List<BestTeamResult>> AnalyzeAsync(Stream gb20CsvStream, BattleContext context)
        {
            var ingestionResult = await _ingestion.ReadAccountsAsync(gb20CsvStream);
            var accounts = ingestionResult.Accounts;

            var results = new List<BestTeamResult>();
            foreach (var account in accounts)
            {
                results.Add(_optimizer.FindBestTeam(account, context));
            }

            return results
                .OrderByDescending(r => r.Score)
                .ToList();
        }
    }
}
