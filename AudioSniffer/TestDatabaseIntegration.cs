using Microsoft.EntityFrameworkCore;
using AudioSniffer.Data;
using AudioSniffer.Models;
using AudioSniffer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioSniffer;

public static class TestDatabaseIntegration
{
    public static async Task RunTestAsync(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        var testResult = new AnalysisResult
        {
            AudioFileId = "test_2026",
            OverallConfidence = 0.85f,
            IsNeuralNetwork = true,
            Detections = new List<DetectionResult>(),
            Metadata = new AudioMetadata
            {
                DurationSeconds = 30.5f,
                SampleRate = 44100,
                Channels = 2,
                Format = "wav"
            }
        };

        var nullLogger = NullLogger<RequestHistoryService>.Instance;

        IRequestHistoryService service = new RequestHistoryService(dbFactory, nullLogger);

        await service.SaveAnalysisAsync(testResult);

        await using var context = await dbFactory.CreateDbContextAsync();
        var histories = await context.RequestHistories.ToListAsync();

        Console.WriteLine($"Сохранено записей: {histories.Count}");
    }
}