using AudioSniffer.Models;

namespace AudioSniffer.Services;

public interface IAudioAnalysisService
{
    Task<(string ResultText, AnalysisResult? Result)> AnalyzeAudioAsync(byte[] audioData, string fileName);
    Task<(string ResultText, BatchAnalysisResult? Result)> AnalyzeZipAsync(byte[] zipData, string fileName);
    Task<float[]> GetWaveformDataAsync(byte[] audioData, string fileName);
}
