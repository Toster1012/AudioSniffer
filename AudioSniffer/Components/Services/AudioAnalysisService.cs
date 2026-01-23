using System.Diagnostics;
using System.Text;
using System.Text.Json;

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

    public async Task<string> AnalyzeAudioAsync(byte[] audioData, string fileName)
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
                    var _fileContent = new ByteArrayContent(audioData);
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
                    var _result = JsonSerializer.Deserialize<AnalysisResponse>(_resultJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var _overallConfidence = _result?.OverallConfidence ?? 0;
                    var _isSuspicious = _result?.IsSuspicious ?? false;

                    if (_isSuspicious)
                    {
                        return $"Аудио возможно сгенерировано процент: {_overallConfidence:P0}";
                    }
                    else
                    {
                        return $"Аудио не было сгенерировано, процент: {(1 - _overallConfidence):P0}";
                    }
                }
                catch (HttpRequestException ex) when (attempt < _maxRetries)
                {
                    _logger.LogWarning(ex, "Connection attempt {Attempt}/{MaxRetries} failed, retrying...", attempt, _maxRetries);
                    await Task.Delay(_retryDelayMs);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while analyzing audio after {MaxRetries} attempts: {FileName}", _maxRetries, fileName);
            return "Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing audio file: {FileName}", fileName);
            return "Ошибка при обработке аудио";
        }

        return "Не удалось подключиться к серверу анализа. Проверьте, что бэкенд запущен на localhost:5000";
    }

    public async Task<float[]> GetWaveformDataAsync(byte[] audioData)
    {
        try
        {
            await Task.Delay(100);

            var _samples = new List<float>();
            var _sampleCount = 200;
            var _stepSize = Math.Max(1, audioData.Length / _sampleCount);

            for (int i = 0; i < audioData.Length; i += _stepSize)
            {
                if (_samples.Count >= _sampleCount)
                    break;

                float _sum = 0;
                int _count = 0;

                for (int j = i; j < Math.Min(i + _stepSize, audioData.Length); j++)
                {
                    float _value = (audioData[j] - 128) / 128f;
                    _sum += Math.Abs(_value);
                    _count++;
                }

                float _avgAmplitude = _count > 0 ? _sum / _count : 0;
                _samples.Add(_avgAmplitude * (i % 2 == 0 ? 1f : -1f));
            }

            if (_samples.Count > 0)
            {
                var _maxValue = _samples.Max(s => Math.Abs(s));
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting waveform data");
            throw;
        }
    }

    private class AnalysisResponse
    {
        public string AudioFileId { get; set; } = string.Empty;
        public float OverallConfidence { get; set; }
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