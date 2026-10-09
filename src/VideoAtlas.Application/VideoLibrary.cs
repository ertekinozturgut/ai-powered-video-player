using System.Security.Cryptography;
using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public sealed record LibraryLimits(long MaxUploadBytes, long UserQuotaBytes);
public sealed record UploadCommand(string Owner, string Title, long Length, bool Consent, Stream Content, VideoContext? Context = null, bool FullReview = false);
public sealed record ShareCommand(int Days, IReadOnlyList<string> Origins, SharePresentation? Presentation = null,
    bool RequireLogin = false, IReadOnlyList<string>? Viewers = null);
public interface IShareAudience
{
    Task<IReadOnlyList<string>> ResolveAsync(IReadOnlyList<string> emails, CancellationToken ct);
}
public sealed class MutationGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}
public sealed class VideoLibrary(IVideoStore videos, ILibraryStore library, IMediaStorage files,
    LibraryLimits limits, TimeProvider clock, IShareAudience? audience = null)
{
    // Caller holds MutationGate for mutations. Upload is serialised to make quota check + insertion atomic.
    public async Task<VideoSummary> UploadAsync(UploadCommand command, CancellationToken ct)
    {
        if (!command.Consent) throw new AppException(400, "consent_required", "Video işleme ve varsa AI hizmetine aktarım için onay gereklidir.");
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 180)
            throw new AppException(400, "invalid_title", "Başlık 1–180 karakter olmalıdır.");
        if (command.Length <= 0 || command.Length > limits.MaxUploadBytes)
            throw new AppException(413, "upload_limit", "Video boş veya 512 MiB yükleme sınırını aşıyor.");
        if (await library.UsedBytesAsync(command.Owner, ct) + command.Length > limits.UserQuotaBytes)
            throw new AppException(409, "storage_quota", "Kütüphane kotanız doldu. Önce eski videolardan bazılarını silin.");
        var video = new Video(Guid.NewGuid(), command.Owner, command.Title.Trim(), command.Length, clock.GetUtcNow());
        if (command.Context is not null) video.SetContext(TranscriptQuality.ValidateContext(command.Context.Terms, command.Context.Instructions));
        if (command.FullReview) video.ConfigureFullReview(true);
        try { await files.SaveUploadAsync(video.Id, command.Content, ct); await videos.AddAsync(video, ct); }
        catch (Exception) { files.Delete(video.Id); throw; }
        return VideoMapping.Summary(video);
    }
    public async Task DeleteAsync(Video video, CancellationToken ct)
    {
        VideoAccess.RequireIdle(video);
        files.Delete(video.Id);
        await library.DeleteAsync(video, ct);
    }
    public async Task RetryAsync(Video video, CancellationToken ct)
    {
        VideoAccess.RequireIdle(video);
        video.Requeue();
        await videos.SaveAsync(ct);
    }
    public async Task ImportAsync(Video video, string srt, CancellationToken ct)
    {
        VideoAccess.RequireIdle(video);
        if (!video.MediaReady) throw new AppException(409, "media_not_ready", "Önce video dosyasının hazırlanması gerekiyor.");
        var segments = Subtitles.Parse(srt, video.Duration);
        video.RecordTranscript(segments, clock.GetUtcNow(), "SRT içe aktarma");
        video.Complete(segments, TranscriptSearch.LocalChapters(segments), "keywords");
        video.SetIssues(TranscriptQuality.Detect(segments, video.Context));
        video.ResetProcessingSteps();
        video.Step("speech", "SRT metni içe aktarıldı", "done", clock.GetUtcNow());
        video.Step("topics", "Yerel konu başlıkları hazırlandı", "done", clock.GetUtcNow());
        if (video.FullReviewEnabled) video.Requeue();
        await videos.SaveAsync(ct);
    }
    public async Task<string> ShareAsync(Video video, ShareCommand command, CancellationToken ct)
    {
        VideoAccess.RequireReady(video);
        if (command.Days is < 1 or > 90 || command.Origins.Count is 0 or > 10)
            throw new AppException(400, "invalid_share", "Paylaşım 1–90 gün ve en fazla 10 site için açılabilir.");
        var origins = command.Origins.Select(ValidateOrigin).Distinct().ToArray();
        var presentation = command.Presentation ?? new();
        if (presentation.Theme is not ("orbital" or "minimal") || !double.IsFinite(presentation.StartAt) || presentation.StartAt < 0 || presentation.StartAt > video.Duration)
            throw new AppException(400, "invalid_share", "Gömme teması veya başlangıç zamanı geçersiz.");
        IReadOnlyList<string> viewers = [];
        if (command.RequireLogin)
        {
            if (command.Viewers is null || command.Viewers.Count is 0 or > 10 || audience is null)
                throw new AppException(400, "invalid_audience", "Giriş gerektiren paylaşım için 1–10 mevcut hesap seçin.");
            viewers = await audience.ResolveAsync(command.Viewers, ct);
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        video.ConfigureShare(presentation, command.RequireLogin, viewers);
        video.Share(VideoAccess.Hash(token), clock.GetUtcNow().AddDays(command.Days), command.RequireLogin ? "'none'" : string.Join(' ', origins));
        await videos.SaveAsync(ct);
        return token;
    }
    public async Task RevokeAsync(Video video, CancellationToken ct)
    { video.RevokeShare(); await videos.SaveAsync(ct); }
    public static string ValidateOrigin(string origin)
    {
        if (origin == "*") return "*";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new AppException(400, "invalid_origin", "Gömme sitesi https://site.example biçiminde olmalıdır. Tüm siteler için * kullanın.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
