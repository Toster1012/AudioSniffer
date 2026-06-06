using AudioSniffer.Models;

namespace AudioSniffer.Services;

public class RequestHistoryService : IRequestHistoryService
{
    private readonly List<AnalysisResult> _history = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const int MaxHistory = 500;

    public async Task SaveAnalysisAsync(AnalysisResult result)
    {
        await _lock.WaitAsync();
        try
        {
            _history.Insert(0, result);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(_history.Count - 1);
        }
        finally { _lock.Release(); }
    }

    public async Task<List<AnalysisResult>> GetHistoryAsync(int limit = 100)
    {
        await _lock.WaitAsync();
        try { return _history.Take(limit).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<AdminStats> GetStatsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var total = _history.Count;
            var ai = _history.Count(r => r.IsAiGenerated);
            var real = total - ai;
            var avgConf = total > 0 ? _history.Average(r => r.OverallConfidence) : 0f;

            var dailyCounts = _history
                .GroupBy(r =>
                {
                    // Используем timestamp из AudioFileId если возможно
                    return DateTime.UtcNow.ToString("yyyy-MM-dd");
                })
                .Select(g => new DailyCount
                {
                    Date = g.Key,
                    Generated = g.Count(r => r.IsAiGenerated),
                    Real = g.Count(r => !r.IsAiGenerated)
                })
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
