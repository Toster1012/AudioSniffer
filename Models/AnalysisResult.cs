using System.Text.Json.Serialization;

namespace AudioSniffer.Models;

public sealed class AnalysisResult
{
    [JsonPropertyName("audio_file_id")]
    public string AudioFileId { get; init; } = string.Empty;

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("overall_confidence")]
    public float OverallConfidence { get; init; }

    [JsonPropertyName("is_suspicious")]
    public bool IsSuspicious { get; init; }
    [JsonPropertyName("is_neural_network")]
    public bool IsNeuralNetwork { get; init; }

    [JsonPropertyName("detections")]
    public List<DetectionResult> Detections { get; init; } = new();

    [JsonPropertyName("metadata")]
    public AudioMetadata Metadata { get; init; } = new();

    [JsonPropertyName("spectrogram_data")]
    public SpectrogramData? SpectrogramData { get; init; }

    public bool IsAiGenerated => IsSuspicious || IsNeuralNetwork;
}

public sealed class SpectrogramData
{
    [JsonPropertyName("frequencies")]
    public List<float> Frequencies { get; init; } = new();

    [JsonPropertyName("times")]
    public List<float> Times { get; init; } = new();

    [JsonPropertyName("magnitudes")]
    public List<List<float>> Magnitudes { get; init; } = new();

    [JsonPropertyName("suspicious_regions")]
    public List<SuspiciousRegion> SuspiciousRegions { get; init; } = new();
}

public sealed class SuspiciousRegion
{
    [JsonPropertyName("start")]
    public float Start { get; init; }

    [JsonPropertyName("end")]
    public float End { get; init; }

    [JsonPropertyName("intensity")]
    public float Intensity { get; init; }
}

public sealed class DetectionResult
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("markers")]
    public List<TimeMarker> Markers { get; init; } = new();

    [JsonPropertyName("additional_data")]
    public Dictionary<string, object> AdditionalData { get; init; } = new();
}

public sealed class TimeMarker
{
    [JsonPropertyName("start_time")]
    public float StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public float EndTime { get; init; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

public sealed class AudioMetadata
{
    [JsonPropertyName("duration_seconds")]
    public float DurationSeconds { get; init; }

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; init; }

    [JsonPropertyName("channels")]
    public int Channels { get; init; }

    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;
}

public sealed class BatchAnalysisResult
{
    [JsonPropertyName("total_files")]
    public int TotalFiles { get; init; }

    [JsonPropertyName("analyzed_files")]
    public int AnalyzedFiles { get; init; }

    [JsonPropertyName("failed_files")]
    public int FailedFiles { get; init; }

    [JsonPropertyName("results")]
    public List<AnalysisResult> Results { get; init; } = new();

    [JsonPropertyName("errors")]
    public List<BatchError> Errors { get; init; } = new();
}

public sealed class BatchError
{
    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}
