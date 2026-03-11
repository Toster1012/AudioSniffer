using Microsoft.EntityFrameworkCore;
using AudioSniffer.Data;
using AudioSniffer.Models;
using System.Text.Json;

namespace AudioSniffer.Services;

public sealed class RequestHistoryService : IRequestHistoryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<RequestHistoryService> _logger;

    public RequestHistoryService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<RequestHistoryService> logger)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAnalysisAsync(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _logger.LogInformation("Попытка сохранения анализа для {AudioFileId}", result.AudioFileId);

        await using var context = await _dbFactory.CreateDbContextAsync();

        var history = new RequestHistory
        {
            AudioFileId = result.AudioFileId,
            OverallConfidence = result.OverallConfidence,
            IsNeuralNetwork = result.IsNeuralNetwork,
            DetectionsJson = JsonSerializer.Serialize(result.Detections),
            DurationSeconds = result.Metadata.DurationSeconds,
            SampleRate = result.Metadata.SampleRate,
            Format = result.Metadata.Format
        };

        context.RequestHistories.Add(history);

        try
        {
            var count = await context.SaveChangesAsync();
            _logger.LogInformation("Сохранено {Count} записей для {AudioFileId}", count, result.AudioFileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении анализа для {AudioFileId}", result.AudioFileId);
            throw;
        }
    }

    public async Task<List<RequestHistory>> GetAllAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var items = await context.RequestHistories
            .OrderByDescending(h => h.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        _logger.LogInformation("Получено {Count} записей из истории", items.Count);

        return items;
    }
}