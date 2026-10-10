using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

public sealed record SystemAdminSummary(int Users, int ActiveUsers, int LockedUsers, int Administrators,
    int Videos, int Queued, int Processing, int Ready, int Failed, int AwaitingTranscript);
public sealed record SystemAdminUserView(string Id, string Email, string DisplayName, string Status, bool EmailConfirmed,
    bool IsAdministrator, int AccessFailures, int VideoCount, int ReadyCount, int FailedCount, double TotalMinutes,
    DateTimeOffset? LastUploadAt);
public sealed record SystemAdminVideoView(Guid Id, string Title, string OwnerEmail, DateTimeOffset CreatedAt,
    VideoStatus Status, string Stage, int Progress, double Duration, int SegmentCount, int ChapterCount,
    string AnalysisLanguage, string AnalysisMode, bool SpeakerDiarization, int Attempts, string? ErrorCode);
public sealed record SystemAdminErrorView(DateTimeOffset CreatedAt, string Source, Guid VideoId, string VideoTitle,
    string OwnerEmail, string Status, string? ErrorCode, string Detail, double? Milliseconds);
public sealed record SystemAdminDashboardView(SystemAdminSummary Summary, IReadOnlyList<SystemAdminUserView> Users,
    IReadOnlyList<SystemAdminVideoView> Videos, IReadOnlyList<SystemAdminErrorView> Errors, string Query, string Status);
public sealed record SystemAdminUsageView(DateTimeOffset CreatedAt, string Provider, string Model, string Operation,
    string Capability, string Status, string? ErrorCode, long InputTokens, long OutputTokens, double? AudioSeconds,
    double? EstimatedUsd, double? ReportedUsd, double Milliseconds);
public sealed record SystemAdminVideoDetailView(Guid Id, string Title, string OwnerEmail, DateTimeOffset CreatedAt,
    VideoStatus Status, string Stage, int Progress, long SourceBytes, double Duration, string AnalysisLanguage,
    string AnalysisMode, bool SpeakerDiarization, int? ExpectedSpeakerCount, int Attempts, string? ErrorCode,
    DateTimeOffset? DeleteAfter, IReadOnlyDictionary<string, string> SpeakerNames,
    IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Chapter> Chapters,
    IReadOnlyList<ProcessingStep> Steps, IReadOnlyList<SystemAdminUsageView> Usage);

[Authorize(Policy = "Administrator"), Route("admin/system")]
public sealed class SystemAdminController(AppDbContext db, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? status, CancellationToken ct)
    {
        q = (q ?? "").Trim();
        if (q.Length > 120) q = q[..120];
        status = (status ?? "").Trim();
        var now = clock.GetUtcNow();

        var users = await db.Users.AsNoTracking().OrderBy(x => x.Email).ToListAsync(ct);
        var preferences = await db.UserLanguagePreferences.AsNoTracking()
            .ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        var administratorRoleId = await db.Roles.AsNoTracking().Where(x => x.Name == "Administrator")
            .Select(x => x.Id).SingleOrDefaultAsync(ct);
        var administratorIds = administratorRoleId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await db.UserRoles.AsNoTracking().Where(x => x.RoleId == administratorRoleId)
                .Select(x => x.UserId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var videoAggregates = await db.Videos.AsNoTracking().GroupBy(x => x.OwnerId).Select(group => new
        {
            OwnerId = group.Key,
            Count = group.Count(),
            Ready = group.Count(x => x.Status == VideoStatus.Ready),
            Failed = group.Count(x => x.Status == VideoStatus.Failed),
            Minutes = group.Sum(x => x.Duration) / 60d,
            LastUpload = group.Max(x => (DateTimeOffset?)x.CreatedAt)
        }).ToDictionaryAsync(x => x.OwnerId, ct);

        var ownerEmails = users.ToDictionary(x => x.Id, x => x.Email ?? x.UserName ?? "—", StringComparer.Ordinal);
        var userRows = users.Where(x => q.Length == 0 || (x.Email ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || preferences.GetValueOrDefault(x.Id, "").Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(100).Select(x =>
            {
                videoAggregates.TryGetValue(x.Id, out var aggregate);
                var locked = x.LockoutEnd is { } end && end > now;
                return new SystemAdminUserView(x.Id, x.Email ?? x.UserName ?? "—", preferences.GetValueOrDefault(x.Id, ""),
                    locked ? "Kilitli" : "Aktif", x.EmailConfirmed, administratorIds.Contains(x.Id), x.AccessFailedCount,
                    aggregate?.Count ?? 0, aggregate?.Ready ?? 0, aggregate?.Failed ?? 0, aggregate?.Minutes ?? 0,
                    aggregate?.LastUpload);
            }).ToList();

        var videoQuery = db.Videos.AsNoTracking().AsQueryable();
        if (Enum.TryParse<VideoStatus>(status, true, out var parsedStatus)) videoQuery = videoQuery.Where(x => x.Status == parsedStatus);
        if (q.Length > 0)
        {
            var matchingOwners = users.Where(x => (x.Email ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id).ToArray();
            videoQuery = videoQuery.Where(x => x.Title.Contains(q) || matchingOwners.Contains(x.OwnerId));
        }
        var recentVideos = await videoQuery.OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        var videoRows = recentVideos.Select(x => MapVideo(x, ownerEmails.GetValueOrDefault(x.OwnerId, "Silinmiş hesap"))).ToList();

        var failedUsage = await db.AiUsage.AsNoTracking().Where(x => x.Status != "success" || x.ErrorCode != null)
            .OrderByDescending(x => x.CreatedAt).Take(75).ToListAsync(ct);
        var usageVideoIds = failedUsage.Select(x => x.VideoId).Distinct().ToArray();
        var usageVideos = await db.Videos.AsNoTracking().Where(x => usageVideoIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title }).ToDictionaryAsync(x => x.Id, ct);
        var usageErrors = failedUsage.Select(x => new SystemAdminErrorView(x.CreatedAt, "AI", x.VideoId,
            usageVideos.GetValueOrDefault(x.VideoId)?.Title ?? "Silinmiş video", ownerEmails.GetValueOrDefault(x.OwnerId, "Silinmiş hesap"),
            x.Status, x.ErrorCode, $"{x.Provider} · {x.Capability} · {x.Operation}", x.Milliseconds));
        var videoErrors = recentVideos.Where(x => x.Status == VideoStatus.Failed || x.ProcessingSteps.Any(s => s.State == "failed"))
            .SelectMany(x => x.ProcessingSteps.Where(s => s.State == "failed")
                .Select(s => new SystemAdminErrorView(s.EndedAt ?? s.StartedAt, "İş", x.Id, x.Title,
                    ownerEmails.GetValueOrDefault(x.OwnerId, "Silinmiş hesap"), s.State, s.ErrorCode ?? x.ErrorCode, s.Label, null))
                .DefaultIfEmpty(new SystemAdminErrorView(x.CreatedAt, "İş", x.Id, x.Title,
                    ownerEmails.GetValueOrDefault(x.OwnerId, "Silinmiş hesap"), x.Status.ToString(), x.ErrorCode, x.Stage, null)));
        var errors = usageErrors.Concat(videoErrors).OrderByDescending(x => x.CreatedAt).Take(100).ToList();

        var statusCounts = await db.Videos.AsNoTracking().GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var lockedUsers = users.Count(x => x.LockoutEnd is { } end && end > now);
        var summary = new SystemAdminSummary(users.Count, users.Count - lockedUsers, lockedUsers, administratorIds.Count,
            statusCounts.Values.Sum(), statusCounts.GetValueOrDefault(VideoStatus.Queued), statusCounts.GetValueOrDefault(VideoStatus.Processing),
            statusCounts.GetValueOrDefault(VideoStatus.Ready), statusCounts.GetValueOrDefault(VideoStatus.Failed),
            statusCounts.GetValueOrDefault(VideoStatus.AwaitingTranscript));
        return View(new SystemAdminDashboardView(summary, userRows, videoRows, errors, q, status));
    }

    [HttpGet("videos/{id:guid}")]
    public async Task<IActionResult> Video(Guid id, CancellationToken ct)
    {
        var video = await db.Videos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (video is null) return NotFound();
        var ownerEmail = await db.Users.AsNoTracking().Where(x => x.Id == video.OwnerId)
            .Select(x => x.Email ?? x.UserName).SingleOrDefaultAsync(ct) ?? "Silinmiş hesap";
        var usage = await db.AiUsage.AsNoTracking().Where(x => x.VideoId == id).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new SystemAdminUsageView(x.CreatedAt, x.Provider, x.Model, x.Operation, x.Capability, x.Status,
                x.ErrorCode, x.InputTokens, x.OutputTokens, x.AudioSeconds, x.EstimatedUsd, x.ReportedUsd, x.Milliseconds))
            .ToListAsync(ct);
        return View(new SystemAdminVideoDetailView(video.Id, video.Title, ownerEmail, video.CreatedAt, video.Status,
            video.Stage, video.Progress, video.SourceBytes, video.Duration, video.AnalysisLanguage, video.AnalysisMode,
            video.SpeakerDiarizationEnabled, video.ExpectedSpeakerCount, video.ProcessingAttempts, video.ErrorCode,
            video.DeleteAfter, video.SpeakerNames, video.Segments, video.Chapters, video.ProcessingSteps, usage));
    }

    private static SystemAdminVideoView MapVideo(Video video, string ownerEmail) => new(video.Id, video.Title, ownerEmail,
        video.CreatedAt, video.Status, video.Stage, video.Progress, video.Duration, video.Segments.Count, video.Chapters.Count,
        video.AnalysisLanguage, video.AnalysisMode, video.SpeakerDiarizationEnabled, video.ProcessingAttempts, video.ErrorCode);
}
