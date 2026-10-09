using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class AiUsageContext : IAiUsageContext
{
    public Guid? VideoId { get; private set; }
    public string OwnerId { get; private set; } = "";
    public string Operation { get; private set; } = "ai";
    public double? AudioSeconds { get; private set; }
    public void SetVideo(Video video) { VideoId = video.Id; OwnerId = video.OwnerId; }
    public void SetOperation(string operation, double? audioSeconds = null) { Operation = operation; AudioSeconds = audioSeconds; }
}
public sealed class AiUsageEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VideoId { get; set; }
    public string OwnerId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Capability { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? ErrorCode { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public bool TokensReported { get; set; }
    public double? AudioSeconds { get; set; }
    public double? EstimatedUsd { get; set; }
    public double? ReportedUsd { get; set; }
    public double Milliseconds { get; set; }
}
public sealed class UsageOptions
{
    public int MaxCallsPerVideoDay { get; set; } = 250;
    public int MaxCallsPerOwnerDay { get; set; } = 1000;
    public double MaxEstimatedUsdPerOwnerDay { get; set; } = 10;
    // Editable planning assumptions, NOT live tariffs or invoices. Unknown models stay unpriced.
    public List<ProviderRate> Rates { get; set; } =
    [
        new("openrouter", "openai/gpt-4.1-mini", .4, 1.6, null),
        new("openrouter", "openai/text-embedding-3-small", .02, 0, null),
        new("openai", "gpt-4o-mini", .15, .6, null),
        new("openai", "text-embedding-3-small", .02, 0, null),
        new("openai", "whisper-1", 0, 0, .36),
        new("elevenlabs", "scribe_v1", 0, 0, .4)
    ];
}
public sealed record ProviderRate(string Provider, string Model, double InputPerMillion, double OutputPerMillion, double? AudioPerHour);
public sealed record UsageSummary(int Calls, int FailedOrUncertain, long InputTokens, long OutputTokens,
    double AudioMinutes, double EstimatedUsd, double ReportedUsd, int ReportedCostCalls, int UnpricedCalls);

public sealed class AiUsageRecorder(IServiceScopeFactory scopes, IOptions<UsageOptions> options,
    TimeProvider clock, ILogger<AiUsageRecorder> logger) : IDisposable
{
    private readonly SemaphoreSlim reservationGate = new(1, 1);
    public async Task<AiUsageEntry?> ReserveAsync(AiUsageContext context, ProviderConnection connection, string path, HttpContent content, CancellationToken ct)
    {
        if (context.VideoId is not { } id) return null;
        var capability = content is MultipartFormDataContent ? "speech" : path.Contains("embedding", StringComparison.Ordinal) ? "embedding" : "chat";
        long input = 0, output = capability == "chat" ? 2500 : 0;
        if (content is not MultipartFormDataContent)
        {
            var payload = await content.ReadAsStringAsync(ct);
            input = (long)Math.Ceiling(payload.Length / 3d);
            if (connection.Kind == "n8n")
            {
                using var data = JsonDocument.Parse(payload);
                if (data.RootElement.TryGetProperty("operation", out var op) && op.GetString() == "embedding") { capability = "embedding"; output = 0; }
            }
        }
        var row = new AiUsageEntry { VideoId = id, OwnerId = context.OwnerId, CreatedAt = clock.GetUtcNow(), Provider = connection.Kind,
            Model = connection.Model, Operation = context.Operation, Capability = capability, InputTokens = input, OutputTokens = output,
            AudioSeconds = capability == "speech" ? context.AudioSeconds : null };
        row.EstimatedUsd = Estimate(row);
        await reservationGate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var day = new DateTimeOffset(row.CreatedAt.UtcDateTime.Date, TimeSpan.Zero);
            var daily = db.AiUsage.Where(u => u.OwnerId == row.OwnerId && u.CreatedAt >= day);
            var settings = options.Value;
            if (await daily.CountAsync(ct) >= settings.MaxCallsPerOwnerDay ||
                await daily.CountAsync(u => u.VideoId == id, ct) >= settings.MaxCallsPerVideoDay ||
                await daily.SumAsync(u => u.EstimatedUsd ?? 0, ct) + (row.EstimatedUsd ?? 0) > settings.MaxEstimatedUsdPerOwnerDay)
                throw new AppException(429, "ai_budget", "Günlük AI çağrı/tahmini bütçe sınırına ulaşıldı. Tamamlanan işler korundu; bütçeyi yöneticiyle kontrol edin.");
            db.AiUsage.Add(row); await db.SaveChangesAsync(ct); return row;
        }
        finally { reservationGate.Release(); }
    }
    public async Task FinishAsync(AiUsageEntry? entry, JsonElement? response, string status, string? error)
    {
        if (entry is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.AiUsage.FindAsync([entry.Id], timeout.Token);
            if (row is null) return;
            row.Status = status; row.ErrorCode = error;
            row.Milliseconds = Math.Max(0, (clock.GetUtcNow() - row.CreatedAt).TotalMilliseconds);
            if (response is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var input) && input.TryGetInt64(out var inputCount) && inputCount >= 0)
                { row.InputTokens = inputCount; row.TokensReported = true; }
                else if (row.Capability == "embedding" && usage.TryGetProperty("total_tokens", out input) && input.TryGetInt64(out inputCount) && inputCount >= 0)
                { row.InputTokens = inputCount; row.TokensReported = true; }
                if (usage.TryGetProperty("completion_tokens", out var output) && output.TryGetInt64(out var outputCount) && outputCount >= 0)
                    row.OutputTokens = outputCount;
                if (row.Provider == "openrouter" && usage.TryGetProperty("cost", out var cost) && cost.TryGetDouble(out var usd) && double.IsFinite(usd) && usd >= 0)
                    row.ReportedUsd = usd;
            }
            row.EstimatedUsd = Estimate(row);
            await db.SaveChangesAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is DbUpdateException or OperationCanceledException or InvalidOperationException or System.Data.Common.DbException)
        { logger.LogWarning("AI usage finalization unavailable: {UsageId}", entry.Id); }
    }
    private double? Estimate(AiUsageEntry row)
    {
        if (row.Operation == "alignment") return null;
        var rate = options.Value.Rates.FirstOrDefault(r => r.Provider == row.Provider && r.Model == row.Model);
        if (rate is null) return null;
        return row.Capability == "speech" ? row.AudioSeconds * rate.AudioPerHour / 3600
            : (row.InputTokens * rate.InputPerMillion + row.OutputTokens * rate.OutputPerMillion) / 1_000_000;
    }
    public static UsageSummary Summarize(IReadOnlyList<AiUsageEntry> rows) => new(rows.Count,
        rows.Count(r => r.Status != "success"), rows.Sum(r => r.InputTokens), rows.Sum(r => r.OutputTokens),
        rows.Sum(r => r.AudioSeconds ?? 0) / 60, rows.Sum(r => r.EstimatedUsd ?? 0), rows.Sum(r => r.ReportedUsd ?? 0),
        rows.Count(r => r.ReportedUsd.HasValue), rows.Count(r => !r.EstimatedUsd.HasValue));
    public void Dispose() => reservationGate.Dispose();
}
