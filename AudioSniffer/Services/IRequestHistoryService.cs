using AudioSniffer.Models;

namespace AudioSniffer.Services;

public interface IRequestHistoryService
{
    Task SaveAnalysisAsync(AnalysisResult result);
    Task<List<RequestHistory>> GetAllAsync();
}