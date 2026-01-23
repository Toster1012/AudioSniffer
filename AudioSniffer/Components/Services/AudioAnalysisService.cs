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

    public async Task<string> AnalyzeAudioAsync(byte[] audio, string fileName)
    {
        int _maxRetries = 3;
        int _retryDelayMs = 2000;

        try
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    using var _content = new MultipartFormDataContent();
                    ByteArrayContent _fileContent = new ByteArrayContent(audio);
                    _fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                    _content.Add(_fileContent, "file", fileName);

                    var _response = await _httpClient.PostAsync("/analyze", _content);

                    if (!_response.IsSuccessStatusCode)
                    {
                        var _error = await _response.Content.ReadAsStringAsync();
                        _logger.LogError("Backend error: {Error}", _error);
                        return "Ошибка анализа аудио";
                    }

                    var _resultJson = await _response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Backend response: {Response}", _resultJson);

                    var _result = JsonSerializer.Deserialize<AnalysisResponse>(_resultJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    float _overallConfidence = _result?.OverallConfidence ?? 0;
                    bool _isSuspicious = _result?.IsSuspicious ?? false;
                    _logger.LogInformation("Parsed values - Confidence: {Confidence}, IsSuspicious: {IsSuspicious}", _overallConfidence, _isSuspicious);

                    if (_isSuspicious)
                    {
                        if (_overallConfidence >= 0.9f)
                            return $"Аудио сгенерировано нейросетью, с вероятностью: {_overallConfidence:P0}";
                        else if (_overallConfidence >= 0.7f)
                            return $"Аудио вероятно сгенерировано нейросетью, с шансом: {_overallConfidence:P0}";
                        else 
                            return $"Аудио возможно сгенерировано нейросетью, с шансом: {_overallConfidence:P0}";
                    }
                    else
                    {
                        return $"Аудио врядли сгенерировано нейросетью, вероятность генерации: {_overallConfidence:P0}";
                    }
                }
                catch (HttpRequestException ex) when (attempt < _maxRetries)
                {
                    _logger.LogWarning(ex, "Connection attempt {Attempt}/{MaxRetries} failed, retrying...", attempt, _maxRetries);
                    await Task.Delay(_retryDelayMs);
                }
            }
        }
        catch (HttpRequestException httpRequestException)
        {
            _logger.LogError(httpRequestException, "HTTP error while analyzing audio after {MaxRetries} attempts: {FileName}", _maxRetries, fileName);
            return "Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error analyzing audio file: {FileName}", fileName);
            return "Ошибка при обработке аудио";
        }

        return "Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000";
    }

    public async Task<float[]> GetWaveformDataAsync(byte[] audio)
    {
        try
        {
            await Task.Delay(100);

            List<float> _samples = new List<float>();
            int _sampleCount = 200;
            int _stepSize = Math.Max(1, audio.Length / _sampleCount);

            for (int i = 0; i < audio.Length; i += _stepSize)
            {
                if (_samples.Count >= _sampleCount)
                    break;

                float _sum = 0;
                int _count = 0;

                for (int j = i; j < Math.Min(i + _stepSize, audio.Length); j++)
                {
                    float _value = (audio[j] - 128) / 128f;
                    _sum += Math.Abs(_value);
                    _count++;
                }

                float _avgAmplitude = _count > 0 ? _sum / _count : 0;
                _samples.Add(_avgAmplitude * (i % 2 == 0 ? 1f : -1f));
            }

            if (_samples.Count > 0)
            {
                float _maxValue = _samples.Max(s => Math.Abs(s));

                if (_maxValue > 0)
                {
                    for (int i = 0; i < _samples.Count; i++)
                    {
                        _samples[i] /= _maxValue;
                    }
                }
            }

            return _samples.ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error extracting waveform data");
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