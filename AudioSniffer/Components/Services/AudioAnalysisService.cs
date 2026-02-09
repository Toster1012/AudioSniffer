using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSniffer.Services;

public class AudioAnalysisService : IAudioAnalysisService
{
    private readonly ILogger<AudioAnalysisService> _logger;
    private readonly HttpClient _httpClient;
    private const string PythonBackendUrl = "http://localhost:5000";

    public AudioAnalysisService(ILogger<AudioAnalysisService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri(PythonBackendUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(5); 
    }

    public async Task<string> AnalyzeAudioAsync(byte[] audio_data, string file_name)
    {
        int max_retry_attempts = 3;
        int retry_delay_milliseconds = 2000;

        try
        {
            for (int current_attempt = 1; current_attempt <= max_retry_attempts; current_attempt++)
            {
                try
                {
                    using MultipartFormDataContent request_content = new MultipartFormDataContent();
                    ByteArrayContent file_content = new ByteArrayContent(audio_data);
                    file_content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                    request_content.Add(file_content, "file", file_name);

                    HttpResponseMessage backend_response = await _httpClient.PostAsync("/analyze", request_content);

                    if (!backend_response.IsSuccessStatusCode)
                    {
                        string error_content = await backend_response.Content.ReadAsStringAsync();
                        _logger.LogError("Backend error: {Error}", error_content);
                        return "Ошибка анализа аудио";
                    }

                    string result_json = await backend_response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Backend response: {Response}", result_json);

                    AnalysisResponse? parsed_result = JsonSerializer.Deserialize<AnalysisResponse>(result_json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    float overall_confidence = parsed_result?.OverallConfidence ?? 0;
                    bool is_suspicious = parsed_result?.IsSuspicious ?? false;
                    _logger.LogInformation("Parsed values - Confidence: {Confidence}, IsSuspicious: {IsSuspicious}", overall_confidence, is_suspicious);

                    if (is_suspicious)
                    {
                        if (overall_confidence >= 0.9f)
                            return $"Аудио сгенерировано нейросетью, с вероятностью: {overall_confidence:P0}";
                        else if (overall_confidence >= 0.7f)
                            return $"Аудио вероятно сгенерировано нейросетью, с шансом: {overall_confidence:P0}";
                        else 
                            return $"Аудио возможно сгенерировано нейросетью, с шансом: {overall_confidence:P0}";
                    }
                    else
                    {
                        return $"Аудио врядли сгенерировано нейросетью, вероятность генерации: {overall_confidence:P0}";
                    }
                }
                catch (HttpRequestException http_exception) when (current_attempt < max_retry_attempts)
                {
                    _logger.LogWarning(http_exception, "Connection attempt {Attempt}/{MaxRetries} failed, retrying...", current_attempt, max_retry_attempts);
                    await Task.Delay(retry_delay_milliseconds);
                }
            }
        }
        catch (HttpRequestException http_request_exception)
        {
            _logger.LogError(http_request_exception, "HTTP error while analyzing audio after {MaxRetries} attempts: {FileName}", max_retry_attempts, file_name);
            return "Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000";
        }
        catch (Exception processing_exception)
        {
            _logger.LogError(processing_exception, "Error analyzing audio file: {FileName}", file_name);
            return "Ошибка при обработке аудио";
        }

        return "Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000";
    }

    public async Task<float[]> GetWaveformDataAsync(byte[] audio_data)
    {
        try
        {
            await Task.Delay(100);

            List<float> audio_samples = new List<float>();
            int target_sample_count = 200;
            int step_size = Math.Max(1, audio_data.Length / target_sample_count);

            for (int byte_index = 0; byte_index < audio_data.Length; byte_index += step_size)
            {
                if (audio_samples.Count >= target_sample_count)
                    break;

                float sample_sum = 0;
                int sample_count = 0;

                for (int offset = byte_index; offset < Math.Min(byte_index + step_size, audio_data.Length); offset++)
                {
                    float normalized_value = (audio_data[offset] - 128) / 128f;
                    sample_sum += Math.Abs(normalized_value);
                    sample_count++;
                }

                float average_amplitude = sample_count > 0 ? sample_sum / sample_count : 0;
                audio_samples.Add(average_amplitude * (byte_index % 2 == 0 ? 1f : -1f));
            }

            if (audio_samples.Count > 0)
            {
                float max_sample_value = audio_samples.Max(sample => Math.Abs(sample));

                if (max_sample_value > 0)
                {
                    for (int sample_index = 0; sample_index < audio_samples.Count; sample_index++)
                    {
                        audio_samples[sample_index] /= max_sample_value;
                    }
                }
            }

            return audio_samples.ToArray();
        }
        catch (Exception waveform_exception)
        {
            _logger.LogError(waveform_exception, "Error extracting waveform data");
            throw;
        }
    }

    private class AnalysisResponse
    {
        public string AudioFileId { get; set; } = string.Empty;
        [JsonPropertyName("overall_confidence")]
        public float OverallConfidence { get; set; }
        [JsonPropertyName("is_suspicious")]
        public bool IsSuspicious { get; set; }
        public List<Detection> Detections { get; set; } = new();
        public Metadata Metadata { get; set; } = new();
    }

    private class Detection
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    private class Metadata
    {
        public float DurationSeconds { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public string Format { get; set; } = string.Empty;
    }
}