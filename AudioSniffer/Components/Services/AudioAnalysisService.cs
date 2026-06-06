using AudioSniffer.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSniffer.Services;

public class AudioAnalysisService : IAudioAnalysisService
{
    private readonly ILogger<AudioAnalysisService> _logger;
    private readonly HttpClient _httpClient;
    private const string PythonBackendUrl = "http://localhost:5000";

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
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri(PythonBackendUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<(string ResultText, AnalysisResult? Result)> AnalyzeAudioAsync(byte[] audio_data, string file_name)
    {
        int max_retries = 3;
        int retry_delay = 2000;

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
                file_content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(content_type);
                request_content.Add(file_content, "file", file_name);

                HttpResponseMessage response = await _httpClient.PostAsync("/analyze", request_content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Backend error {Status}: {Error}", response.StatusCode, error);
                    return ($"Ошибка бэкенда ({(int)response.StatusCode}): {error}", null);
                }

                string json = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Backend response: {Response}", json[..Math.Min(json.Length, 500)]);

                AnalysisResult? result = JsonSerializer.Deserialize<AnalysisResult>(json, JsonOptions);

                if (result == null)
                    return ("Ошибка парсинга ответа от анализатора", null);

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
                return ("Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing {File}", file_name);
                return ($"Ошибка при обработке аудио: {ex.Message}", null);
            }
        }

        return ("Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000", null);
    }

    public async Task<(string ResultText, BatchAnalysisResult? Result)> AnalyzeZipAsync(byte[] zip_data, string file_name)
    {
        try
        {
            using MultipartFormDataContent request_content = new();
            ByteArrayContent file_content = new(zip_data);
            file_content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            request_content.Add(file_content, "file", file_name);

            HttpResponseMessage response = await _httpClient.PostAsync("/analyze/batch", request_content);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Batch backend error {Status}: {Error}", response.StatusCode, error);
                return ($"Ошибка бэкенда: {error}", null);
            }

            string json = await response.Content.ReadAsStringAsync();
            BatchAnalysisResult? result = JsonSerializer.Deserialize<BatchAnalysisResult>(json, JsonOptions);

            if (result == null)
                return ("Ошибка парсинга ответа", null);

            int ai_count = result.Results.Count(r => r.IsAiGenerated);
            string summary = $"Проанализировано {result.AnalyzedFiles} из {result.TotalFiles} файлов. " +
                             $"ИИ-генерация обнаружена в {ai_count} файлах.";

            return (summary, result);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error in batch analysis");
            return ("Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch analysis");
            return ($"Ошибка при обработке архива: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Получает реальную форму волны из Python-бэкенда.
    /// Fallback на простой парсинг байт если бэкенд недоступен.
    /// </summary>
    public async Task<float[]> GetWaveformDataAsync(byte[] audio_data, string file_name)
    {
        try
        {
            string extension = Path.GetExtension(file_name)?.ToLowerInvariant() ?? string.Empty;
            string content_type = AudioContentTypes.TryGetValue(extension, out string? mapped_type)
                ? mapped_type : "audio/mpeg";

            using MultipartFormDataContent request_content = new();
            ByteArrayContent file_content = new(audio_data);
            file_content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(content_type);
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

        // Fallback: грубый парсинг байт (не идеально, но хоть что-то)
        return GenerateFallbackWaveform(audio_data);
    }

    private static float[] GenerateFallbackWaveform(byte[] audio_data)
    {
        int target = 400;
        if (audio_data.Length == 0) return Array.Empty<float>();

        // Пропускаем возможный заголовок (первые 44 байта WAV / 128 байт MP3 ID3)
        int offset = Math.Min(128, audio_data.Length / 10);
        int usable = audio_data.Length - offset;
        if (usable <= 0) return Array.Empty<float>();

        int step = Math.Max(1, usable / target);
        List<float> result = new();
        Random rng = new(42); // фиксированный seed для воспроизводимости

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
            if (confidence >= 0.85f)
                return $"🔴 Аудио сгенерировано нейросетью с высокой вероятностью: {confidence:P0}";
            else if (confidence >= 0.60f)
                return $"🟠 Аудио вероятно сгенерировано нейросетью: {confidence:P0}";
            else
                return $"🟡 Аудио возможно сгенерировано нейросетью: {confidence:P0}";
        }
        else
        {
            if (confidence <= 0.15f)
                return $"🟢 Аудио похоже на живую запись. Вероятность ИИ: {confidence:P0}";
            else
                return $"🟢 Аудио врядли сгенерировано нейросетью. Вероятность ИИ: {confidence:P0}";
        }
    }
}
