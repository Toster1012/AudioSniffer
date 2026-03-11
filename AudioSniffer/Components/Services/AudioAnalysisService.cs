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

    public AudioAnalysisService(ILogger<AudioAnalysisService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri(PythonBackendUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<(string ResultText, AnalysisResult? Result)> AnalyzeAudioAsync(byte[] audio_data, string file_name)
    {
        int max_retry_attempts = 3;
        int retry_delay_milliseconds = 2000;

        try
        {
            for (int current_attempt = 1; current_attempt <= max_retry_attempts; current_attempt++)
            {
                try
                {
                    string extension = Path.GetExtension(file_name)?.ToLowerInvariant() ?? string.Empty;
                    string content_type = AudioContentTypes.TryGetValue(extension, out string? mapped_type)
                        ? mapped_type
                        : "audio/mpeg";

                    using MultipartFormDataContent request_content = new MultipartFormDataContent();
                    ByteArrayContent file_content = new ByteArrayContent(audio_data);
                    file_content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(content_type);
                    request_content.Add(file_content, "file", file_name);

                    HttpResponseMessage backend_response = await _httpClient.PostAsync("/analyze", request_content);

                    if (!backend_response.IsSuccessStatusCode)
                    {
                        string error_content = await backend_response.Content.ReadAsStringAsync();
                        _logger.LogError("Backend error {StatusCode}: {Error}", backend_response.StatusCode, error_content);
                        return ($"Ошибка бэкенда: {error_content}", null);
                    }

                    string result_json = await backend_response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Backend response: {Response}", result_json);

                    BackendAnalysisResponse? parsed_result = JsonSerializer.Deserialize<BackendAnalysisResponse>(result_json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (parsed_result == null)
                        return ("Ошибка анализа аудио", null);

                    float overall_confidence = parsed_result.OverallConfidence;
                    bool is_suspicious = parsed_result.IsSuspicious;

                    _logger.LogInformation("Parsed values - Confidence: {Confidence}, IsSuspicious: {IsSuspicious}", overall_confidence, is_suspicious);

                    var analysis_result = new AnalysisResult
                    {
                        AudioFileId = string.IsNullOrEmpty(parsed_result.AudioFileId) ? file_name : parsed_result.AudioFileId,
                        OverallConfidence = overall_confidence,
                        IsNeuralNetwork = is_suspicious,
                        Detections = parsed_result.Detections.Select(d => new DetectionResult
                        {
                            Type = d.Type,
                            Title = d.Title,
                            Confidence = d.Confidence,
                            Description = d.Description
                        }).ToList(),
                        Metadata = new AudioMetadata
                        {
                            DurationSeconds = parsed_result.Metadata.DurationSeconds,
                            SampleRate = parsed_result.Metadata.SampleRate,
                            Channels = parsed_result.Metadata.Channels,
                            Format = parsed_result.Metadata.Format
                        }
                    };

                    string result_text;
                    if (is_suspicious)
                    {
                        if (overall_confidence >= 0.9f)
                            result_text = $"Аудио сгенерировано нейросетью, с вероятностью: {overall_confidence:P0}";
                        else if (overall_confidence >= 0.7f)
                            result_text = $"Аудио вероятно сгенерировано нейросетью, с шансом: {overall_confidence:P0}";
                        else
                            result_text = $"Аудио возможно сгенерировано нейросетью, с шансом: {overall_confidence:P0}";
                    }
                    else
                    {
                        result_text = $"Аудио врядли сгенерировано нейросетью, вероятность генерации: {overall_confidence:P0}";
                    }

                    return (result_text, analysis_result);
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
            return ("Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000", null);
        }
        catch (Exception processing_exception)
        {
            _logger.LogError(processing_exception, "Error analyzing audio file: {FileName}", file_name);
            return ("Ошибка при обработке аудио", null);
        }

        return ("Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000", null);
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

    private class BackendAnalysisResponse
    {
        [JsonPropertyName("audio_file_id")]
        public string AudioFileId { get; set; } = string.Empty;

        [JsonPropertyName("overall_confidence")]
        public float OverallConfidence { get; set; }

        [JsonPropertyName("is_suspicious")]
        public bool IsSuspicious { get; set; }

        [JsonPropertyName("detections")]
        public List<BackendDetection> Detections { get; set; } = new();

        [JsonPropertyName("metadata")]
        public BackendMetadata Metadata { get; set; } = new();
    }

    private class BackendDetection
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    private class BackendMetadata
    {
        [JsonPropertyName("duration_seconds")]
        public float DurationSeconds { get; set; }

        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; set; }

        [JsonPropertyName("channels")]
        public int Channels { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;
    }
}