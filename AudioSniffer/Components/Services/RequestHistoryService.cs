using AudioSniffer.Models;

namespace AudioSniffer.Services;

public class RequestHistoryService : IRequestHistoryService
{
    private readonly List<(AnalysisResult Result, DateTime Timestamp)> _history = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const int MaxHistory = 500;

    public async Task SaveAnalysisAsync(AnalysisResult result)
    {
        await _lock.WaitAsync();
        try
        {
            _history.Insert(0, (result, DateTime.UtcNow));
            if (_history.Count > MaxHistory)
                _history.RemoveAt(_history.Count - 1);
        }
        finally { _lock.Release(); }
    }

    public async Task<List<AnalysisResult>> GetHistoryAsync(int limit = 100)
    {
        await _lock.WaitAsync();
        try { return _history.Take(limit).Select(x => x.Result).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<AdminStats> GetStatsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var total = _history.Count;
            var ai = _history.Count(x => x.Result.IsAiGenerated);
            var real = total - ai;
            var avgConf = total > 0 ? _history.Average(x => x.Result.OverallConfidence) : 0f;

            var dailyCounts = _history
                .GroupBy(x => x.Timestamp.ToString("yyyy-MM-dd"))
                .Select(g => new DailyCount
                {
                    Date = g.Key,
                    Generated = g.Count(x => x.Result.IsAiGenerated),
                    Real = g.Count(x => !x.Result.IsAiGenerated)
                })
                .OrderByDescending(d => d.Date)
                .Take(30)
                .ToList();

            return new AdminStats
            {
                TotalAnalyses = total,
                AiGenerated = ai,
                RealAudio = real,
                AverageConfidence = avgConf,
                DailyCounts = dailyCounts
            };
        }
        finally { _lock.Release(); }
    }

    public async Task ClearHistoryAsync()
    {
        await _lock.WaitAsync();
        try { _history.Clear(); }
        finally { _lock.Release(); }
    }
}
