namespace AudioSniffer.Services;

public interface IAudioAnalysisService
{
    Task<string> AnalyzeAudioAsync(byte[] audioData, string fileName);

    Task<float[]> GetWaveformDataAsync(byte[] audioData);
}