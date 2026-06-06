using AudioSniffer.Models;

namespace AudioSniffer.Services;

public interface IRequestHistoryService
{
    Task SaveAnalysisAsync(AnalysisResult result);
    Task<List<AnalysisResult>> GetHistoryAsync(int limit = 100);
    Task<AdminStats> GetStatsAsync();
    Task ClearHistoryAsync();
}

public class AdminStats
{
    public int TotalAnalyses { get; set; }
    public int AiGenerated { get; set; }
    public int RealAudio { get; set; }
    public float AverageConfidence { get; set; }
    public List<DailyCount> DailyCounts { get; set; } = new();
}

public class DailyCount
{
    public string Date { get; set; } = "";
    public int Generated { get; set; }
    public int Real { get; set; }
}
