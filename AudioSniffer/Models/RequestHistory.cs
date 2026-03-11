using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AudioSniffer.Models;

public sealed class RequestHistory
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    [Required]
    public string AudioFileId { get; init; } = string.Empty;

    public float OverallConfidence { get; init; }

    public bool IsNeuralNetwork { get; init; }

    [Required]
    public string DetectionsJson { get; init; } = string.Empty;

    public float DurationSeconds { get; init; }

    public int SampleRate { get; init; }

    public string Format { get; init; } = string.Empty;
}