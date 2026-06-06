using AudioSniffer.Models;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSniffer.Services;

public class AudioAnalysisService : IAudioAnalysisService
{
    private readonly ILogger<AudioAnalysisService> _logger;
    private readonly HttpClient _httpClient;
    private const string PythonBackendUrl = "https://localhost:5000";

    private static readonly Dictionary<string, string> AudioContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".mp3",  "audio/mpeg" },
        { ".wav",  "audio/wav"  },
        { ".ogg",  "audio/ogg"  },
        { ".aac",  "audio/aac"  },
        { ".flac", "audio/flac" },
        { ".m4a",  "audio/mp4"  }
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public AudioAnalysisService(ILogger<AudioAnalysisService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        _httpClient = new HttpClient(handler);
        _httpClient.BaseAddress = new Uri(PythonBackendUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<(string ResultText, AnalysisResult? Result)> AnalyzeAudioAsync(byte[] audio_data, string file_name)
    {
        const int max_retries = 3;
        const int retry_delay = 2000;

        for (int attempt = 1; attempt <= max_retries; attempt++)
        {
            try
            {
                string extension = Path.GetExtension(file_name)?.ToLowerInvariant() ?? string.Empty;
                string content_type = AudioContentTypes.TryGetValue(extension, out string? mapped_type)
                    ? mapped_type
                    : "audio/mpeg";

                using MultipartFormDataContent request_content = new();
                ByteArrayContent file_content = new(audio_data);
                file_content.Headers.ContentType = new MediaTypeHeaderValue(content_type);
                request_content.Add(file_content, "file", file_name);

                HttpResponseMessage response = await _httpClient.PostAsync("/analyze", request_content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Backend error {Status}: {Error}", response.StatusCode, error);
                    return ($"Backend error ({(int)response.StatusCode}): {error}", null);
                }

                string json = await response.Content.ReadAsStringAsync();

                AnalysisResult? result = JsonSerializer.Deserialize<AnalysisResult>(json, JsonOptions);

                if (result == null)
                    return ("Error parsing analyzer response", null);

                string result_text = BuildResultText(result.OverallConfidence, result.IsAiGenerated);
                return (result_text, result);
            }
            catch (HttpRequestException ex) when (attempt < max_retries)
            {
                _logger.LogWarning(ex, "Attempt {Attempt}/{Max} failed, retrying...", attempt, max_retries);
                await Task.Delay(retry_delay);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "All {Max} attempts failed for {File}", max_retries, file_name);
                return ("Cannot connect to analysis server. Ensure backend is running on localhost:5000", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing {File}", file_name);
                return ($"Audio processing error: {ex.Message}", null);
            }
        }

        return ("Cannot connect to analysis server. Ensure backend is running on localhost:5000", null);
    }

    public async Task<(string ResultText, BatchAnalysisResult? Result)> AnalyzeZipAsync(byte[] zip_data, string file_name)
    {
        try
        {
            using MultipartFormDataContent request_content = new();
            ByteArrayContent file_content = new(zip_data);
            file_content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            request_content.Add(file_content, "file", file_name);

            HttpResponseMessage response = await _httpClient.PostAsync("/analyze/batch", request_content);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Batch backend error {Status}: {Error}", response.StatusCode, error);
                return ($"Backend error: {error}", null);
            }

            string json = await response.Content.ReadAsStringAsync();
            BatchAnalysisResult? result = JsonSerializer.Deserialize<BatchAnalysisResult>(json, JsonOptions);

            if (result == null)
                return ("Error parsing response", null);

            int ai_count = result.Results.Count(r => r.IsAiGenerated);
            string summary = $"Analyzed {result.AnalyzedFiles} of {result.TotalFiles} files. " +
                             $"AI generation detected in {ai_count} files.";

            return (summary, result);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error in batch analysis");
            return ("Cannot connect to analysis server. Ensure backend is running on localhost:5000", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch analysis");
            return ($"Archive processing error: {ex.Message}", null);
        }
    }

    public async Task<float[]> GetWaveformDataAsync(byte[] audio_data, string file_name)
    {
        try
        {
            string extension = Path.GetExtension(file_name)?.ToLowerInvariant() ?? string.Empty;
            string content_type = AudioContentTypes.TryGetValue(extension, out string? mapped_type)
                ? mapped_type : "audio/mpeg";

            using MultipartFormDataContent request_content = new();
            ByteArrayContent file_content = new(audio_data);
            file_content.Headers.ContentType = new MediaTypeHeaderValue(content_type);
            request_content.Add(file_content, "file", file_name);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            HttpResponseMessage response = await _httpClient.PostAsync("/waveform", request_content, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(cts.Token);
                float[]? samples = JsonSerializer.Deserialize<float[]>(json, JsonOptions);
                if (samples != null && samples.Length > 0)
                    return samples;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Waveform endpoint failed, using fallback");
        }
        return GenerateFallbackWaveform(audio_data);
    }

    private static float[] GenerateFallbackWaveform(byte[] audio_data)
    {
        int target = 400;
        if (audio_data.Length == 0) return Array.Empty<float>();
        int offset = Math.Min(128, audio_data.Length / 10);
        int usable = audio_data.Length - offset;
        if (usable <= 0) return Array.Empty<float>();

        int step = Math.Max(1, usable / target);
        List<float> result = new();

        for (int i = offset; i < audio_data.Length && result.Count < target; i += step)
        {
            float v = (audio_data[i] - 128) / 128f;
            result.Add(v);
        }

        float max = result.Max(v => Math.Abs(v));
        if (max > 0)
            for (int i = 0; i < result.Count; i++)
                result[i] /= max;

        return result.ToArray();
    }

    private static string BuildResultText(float confidence, bool is_ai)
    {
        if (is_ai)
        {
            return confidence >= 0.85f 
                ? $"Audio generated by neural network with high probability: {confidence:P0}"
                : confidence >= 0.60f 
                    ? $"Audio likely generated by neural network: {confidence:P0}"
                    : $"Audio possibly generated by neural network: {confidence:P0}";
        }
        else
        {
            return confidence <= 0.15f
                ? $"Audio appears to be live recording. AI probability: {confidence:P0}"
                : $"Audio unlikely generated by neural network. AI probability: {confidence:P0}";
        }
    }
}
