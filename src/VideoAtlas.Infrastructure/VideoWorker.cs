using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class VideoWorker(IServiceScopeFactory scopes, ILogger<VideoWorker> logger) : BackgroundService
{
    private DateTimeOffset nextRetentionCheck = DateTimeOffset.MinValue;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do { await CleanExpiredAsync(stoppingToken); await ProcessNextAsync(stoppingToken); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var canceled = await db.Videos.Where(v => v.CancelRequested && (v.Status == VideoStatus.Processing || v.Status == VideoStatus.Queued)).ToListAsync(ct);
        foreach (var video in canceled)
        {
            video.Cancel();
            MarkInterruptedSteps(video, "canceled", null, scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
        }
        await db.SaveChangesAsync(ct);
        await db.Videos.Where(v => v.Status == VideoStatus.Processing && !v.CancelRequested)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VideoStatus.Queued), ct);
    }
    private async Task ProcessNextAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gate = scope.ServiceProvider.GetRequiredService<MutationGate>();
        var registry = scope.ServiceProvider.GetRequiredService<VideoJobRegistry>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        using var job = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Video? video;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            video = await db.Videos.OrderBy(v => v.CreatedAt).FirstOrDefaultAsync(v => v.Status == VideoStatus.Queued, ct);
            if (video is null) return;
            video.StartAttempt();
            await db.SaveChangesAsync(ct);
            registry.Register(video.Id, job);
        }
        finally { gate.Semaphore.Release(); }
        try
        {
            await scope.ServiceProvider.GetRequiredService<VideoPipeline>().RunAsync(video, job.Token);
            await gate.Semaphore.WaitAsync(ct);
            try
            {
                if (job.IsCancellationRequested || await db.Videos.AsNoTracking().Where(v => v.Id == video.Id).Select(v => v.CancelRequested).SingleAsync(ct)) video.Cancel();
                await db.SaveChangesAsync(ct);
            }
            finally { gate.Semaphore.Release(); }
        }
        catch (Exception ex) when (ex is MediaException or AiException or HttpRequestException or JsonException or IOException or InvalidOperationException or KeyNotFoundException or FormatException or AppException)
        {
            var code = ex switch { MediaException m => m.Code, AiException a => a.Code, AppException a => a.Code, _ => "processing_failed" };
            logger.LogWarning("Video processing failed: {VideoId} {ErrorCode}", video.Id, code);
            if (job.IsCancellationRequested) video.Cancel(); else video.Fail(code);
            MarkInterruptedSteps(video, job.IsCancellationRequested ? "canceled" : "failed", code, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (job.IsCancellationRequested) video.Cancel(); else video.Fail("ai_timeout");
            MarkInterruptedSteps(video, job.IsCancellationRequested ? "canceled" : "failed", job.IsCancellationRequested ? null : "ai_timeout", clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            registry.Remove(video.Id);
            var audio = scope.ServiceProvider.GetRequiredService<IMediaStorage>().PathFor(video.Id, "audio.mp3");
            if (File.Exists(audio)) File.Delete(audio);
        }
    }
    private static void MarkInterruptedSteps(Video video, string state, string? error, DateTimeOffset now)
    {
        foreach (var step in video.ProcessingSteps.Where(s => s.State == "running").ToArray()) video.Step(step.Key, step.Label, state, now, error);
    }
    private async Task CleanExpiredAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        if (now < nextRetentionCheck) return;
        nextRetentionCheck = now.AddHours(1);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var files = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
        var gate = scope.ServiceProvider.GetRequiredService<MutationGate>();
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await db.AiUsage.Where(u => u.CreatedAt < now.AddDays(-30)).ExecuteDeleteAsync(ct);
            await db.ApiRequestLogs.Where(u => u.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync(ct);
            var expired = await db.Videos.Where(v => v.DeleteAfter != null && v.DeleteAfter <= now && v.Status != VideoStatus.Queued && v.Status != VideoStatus.Processing).Take(20).ToListAsync(ct);
            foreach (var video in expired)
            {
                try { files.Delete(video.Id); db.Videos.Remove(video); await db.SaveChangesAsync(ct); }
                catch (IOException) { logger.LogWarning("Retention cleanup unavailable: {VideoId}", video.Id); }
            }
        }
        finally { gate.Semaphore.Release(); }
    }
}
