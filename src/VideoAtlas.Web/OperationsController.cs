using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

public sealed record QualityJobCommand(int Version, bool Enabled);
public sealed record RetentionCommand(int Version, int Days, bool ConfirmDeletion);
public sealed record VideoOperationsView(int Version, IReadOnlyList<ProcessingStep> Steps, int Attempts, int CompletedChunks,
    int TotalChunks, bool CancelRequested, bool FullReview, int QualityReviewed, int TotalSegments, DateTimeOffset? DeleteAfter,
    UsageSummary Usage, int DailyCallLimit, double DailyEstimatedBudgetUsd);
public sealed record AdminJobView(Guid Id, string Title, VideoStatus Status, string Stage, string? ErrorCode, int Attempts, bool CancelRequested);
public sealed record OperationsAdminView(IReadOnlyList<AdminJobView> Jobs, UsageSummary Today, int DailyCallLimit, double DailyEstimatedBudgetUsd);

[Authorize, ApiController, Route("api/videos/{id:guid}/operations"), RequestSizeLimit(16384)]
public sealed class OperationsController(VideoAccess access, IVideoStore store, AppDbContext db, VideoJobRegistry jobs,
    TimeProvider clock, IOptions<UsageOptions> limits) : ControllerBase
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var v = await access.OwnedAsync(id, Owner, ct);
        var usage = await UsageQueries.SummaryAsync(db.AiUsage.AsNoTracking().Where(u => u.VideoId == id), ct);
        return Ok(new VideoOperationsView(v.ReviewVersion, v.ProcessingSteps, v.ProcessingAttempts, v.TranscriptChunks.Count,
            (int)Math.Ceiling(v.Duration / 45), v.CancelRequested, v.FullReviewEnabled, v.QualityCursor, v.Segments.Count,
            v.DeleteAfter, usage, limits.Value.MaxCallsPerVideoDay, limits.Value.MaxEstimatedUsdPerOwnerDay));
    }
    [HttpPost("cancel"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var v = await access.OwnedAsync(id, Owner, ct);
        if (!v.IsBusy) throw new AppException(409, "not_processing", "Video şu anda işlenmiyor.");
        v.RequestCancellation(); if (v.Status == VideoStatus.Queued) v.Cancel();
        await store.SaveAsync(ct); jobs.Cancel(id); return Accepted();
    }
    [HttpPost("quality"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Quality(Guid id, QualityJobCommand command, CancellationToken ct)
    {
        var v = await EditableAsync(id, command.Version, ct);
        v.ConfigureFullReview(command.Enabled);
        v.SetIssues(v.TranscriptIssues.Where(i => i.Source != "llm-batch"));
        v.Requeue(); await store.SaveAsync(ct); return Accepted();
    }
    [HttpPost("reset-checkpoints"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Reset(Guid id, ReviewCommand command, CancellationToken ct)
    {
        var v = await EditableAsync(id, command.Version, ct);
        if (v.Segments.Count > 0) throw new AppException(409, "transcript_exists", "Tamamlanmış metin varken ses kontrol noktaları sıfırlanamaz.");
        v.ResetChunks(); v.Requeue(); await store.SaveAsync(ct); return Accepted();
    }
    [HttpPost("retention"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Retention(Guid id, RetentionCommand command, CancellationToken ct)
    {
        var v = await EditableAsync(id, command.Version, ct);
        if (command.Days is < 0 or > 365 || command.Days > 0 && !command.ConfirmDeletion)
            throw new AppException(400, "retention_confirmation", "Saklama süresi 0–365 gün olmalı. Otomatik silme için açık onay gerekir; 0 süresiz saklar.");
        v.SetRetention(command.Days == 0 ? null : clock.GetUtcNow().AddDays(command.Days));
        await store.SaveAsync(ct); return NoContent();
    }
    private async Task<Video> EditableAsync(Guid id, int version, CancellationToken ct)
    {
        var v = await access.OwnedAsync(id, Owner, ct); VideoAccess.RequireIdle(v);
        if (v.ReviewVersion != version) throw new AppException(409, "review_conflict", "Kayıt değişti; sayfayı yenileyin.");
        return v;
    }
}

[Authorize(Policy = "Administrator"), Route("admin/operations")]
public sealed class OperationsAdminController(AppDbContext db, TimeProvider clock, IOptions<UsageOptions> options) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var day = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var jobs = await db.Videos.AsNoTracking().OrderByDescending(v => v.CreatedAt).Take(100)
            .Select(v => new AdminJobView(v.Id, v.Title, v.Status, v.Stage, v.ErrorCode, v.ProcessingAttempts, v.CancelRequested)).ToListAsync(ct);
        var totals = await UsageQueries.SummaryAsync(db.AiUsage.AsNoTracking().Where(u => u.CreatedAt >= day), ct);
        return View(new OperationsAdminView(jobs, totals, options.Value.MaxCallsPerOwnerDay, options.Value.MaxEstimatedUsdPerOwnerDay));
    }
}
internal static class UsageQueries
{
    public static async Task<UsageSummary> SummaryAsync(IQueryable<AiUsageEntry> query, CancellationToken ct) =>
        await query.GroupBy(_ => 1).Select(g => new UsageSummary(g.Count(), g.Count(u => u.Status != "success"), g.Sum(u => u.InputTokens),
            g.Sum(u => u.OutputTokens), g.Sum(u => u.AudioSeconds ?? 0) / 60, g.Sum(u => u.EstimatedUsd ?? 0), g.Sum(u => u.ReportedUsd ?? 0),
            g.Count(u => u.ReportedUsd != null), g.Count(u => u.EstimatedUsd == null))).SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}
