using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class VideoPipeline(AppDbContext db, MediaProcessor media, VideoIntelligence ai, IAiProviderFactory providers,
    ITranscriptReviewer reviewer, TranscriptIndex index, IAiUsageContext usage, TimeProvider clock)
{
    public static string SpeechFingerprint(ProviderConnection connection, VideoContext context) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', connection.Kind, connection.Endpoint,
            connection.Model, JsonSerializer.Serialize(context.Terms)))));

    public async Task RunAsync(Video video, CancellationToken ct)
    {
        usage.SetVideo(video);
        await StageAsync(video, "media", "Video ve ses hazırlanıyor", 5, async () =>
        {
            video.SetMedia(await media.PrepareAsync(video.Id, ct));
        }, ct);
        if (video.Segments.Count == 0)
        {
            var route = await providers.SpeechAsync(ct);
            if (route is null) { video.AwaitTranscript(); video.Step("speech", "Konuşmayı metne çevirme", "waiting", clock.GetUtcNow()); return; }
            var fingerprint = SpeechFingerprint(route.Connection, video.Context);
            if (video.TranscriptChunks.Any(c => c.Fingerprint != fingerprint))
                throw new AppException(409, "stt_checkpoint_mismatch", "Tamamlanan ses parçaları farklı STT/model/sözlüğe ait. Eski ayara dönün veya ses kontrol noktalarını açıkça sıfırlayın.");
            await StageAsync(video, "speech", "Konuşmayı metne çevirme", 15, async () =>
            {
                for (var offset = 0d; offset < video.Duration; offset += 480)
                {
                    ct.ThrowIfCancellationRequested();
                    if (video.TranscriptChunks.Any(c => c.Offset == offset)) continue;
                    var end = Math.Min(offset + 480, video.Duration);
                    var audio = await media.AudioAsync(video.Id, offset, ct);
                    if (audio is null) { video.AwaitTranscript(); return; }
                    video.Report($"Ses parçası {video.TranscriptChunks.Count + 1}/{(int)Math.Ceiling(video.Duration / 480)} çözümleniyor", 15 + (int)(45 * offset / video.Duration));
                    await db.SaveChangesAsync(ct);
                    usage.SetOperation("transcription", end - offset);
                    var chunk = await ai.TranscribeAsync(audio, offset, video.Context, ct);
                    var valid = chunk.Where(s => !string.IsNullOrWhiteSpace(s.Text) && s.Start < end)
                        .Select(s => s with { Start = Math.Max(s.Start, offset), End = Math.Min(s.End, end) }).Where(s => s.End > s.Start).ToArray();
                    video.Checkpoint(new(offset, end, fingerprint, valid));
                    // Each completed paid chunk is durable before the next network call.
                    await db.SaveChangesAsync(ct);
                }
                var segments = video.TranscriptChunks.OrderBy(c => c.Offset).SelectMany(c => c.Segments)
                    .Select((s, i) => s with { Id = i }).ToArray();
                if (segments.Length == 0) { video.AwaitTranscript(); return; }
                video.RecordTranscript(segments, clock.GetUtcNow(), "Otomatik transkripsiyon");
                await db.SaveChangesAsync(ct);
            }, ct);
            if (video.Status == VideoStatus.AwaitingTranscript) return;
        }
        else video.Step("speech", "Mevcut metin yeniden kullanıldı", "done", clock.GetUtcNow());

        await StageAsync(video, "quality", "Ses ve transkript kalite kontrolü", 65, async () =>
        {
            if (video.TranscriptIssues.Count == 0) video.SetIssues(TranscriptQuality.Detect(video.Segments, video.Context));
            if (!video.SilenceChecked)
            {
                var silence = await media.SilenceAsync(video.Id, video.Duration, ct);
                video.SetIssues(video.TranscriptIssues.Concat(TranscriptQuality.SilenceIssues(video.Segments, silence)));
                video.MarkSilenceChecked(); await db.SaveChangesAsync(ct);
            }
            // SRT-only use remains fully local when no chat provider is configured. A full
            // review is an optional AI enhancement, not a prerequisite for completing the
            // imported transcript, local topics, extractive chat, or sharing.
            if (video.FullReviewEnabled && await ai.IsConfiguredAsync(ct))
            {
                while (video.QualityCursor < video.Segments.Count)
                {
                    ct.ThrowIfCancellationRequested();
                    var length = 0;
                    var batch = video.Segments.Skip(video.QualityCursor).Take(40)
                        .TakeWhile(s => (length += s.Text.Length) <= 14000 || length == s.Text.Length).ToArray();
                    // One preceding context segment reduces artificial boundary drift.
                    var context = video.Segments.Skip(Math.Max(0, video.QualityCursor - 1)).Take(batch.Length + (video.QualityCursor > 0 ? 1 : 0)).ToArray();
                    usage.SetOperation("quality-scan");
                    var issues = await reviewer.InspectBatchAsync(context, video.Context, ct);
                    video.AdvanceQuality(video.QualityCursor + batch.Length, issues.Where(i => batch.Any(s => s.Id == i.SegmentId)));
                    video.Report($"Kalite incelemesi: {video.QualityCursor}/{video.Segments.Count} konuşma", 65 + (int)(12d * video.QualityCursor / video.Segments.Count));
                    await db.SaveChangesAsync(ct);
                }
            }
        }, ct);
        TopicResult? topics = null;
        await StageAsync(video, "topics", "Bölüm özetleri ve konu başlıkları", 80, async () =>
        { usage.SetOperation("topics"); topics = await ai.CategorizeAsync(video.Segments, video.Context, ct); }, ct);
        IndexBuild? built = null;
        await StageAsync(video, "index", "Anlamsal arama indeksi", 90, async () =>
        { usage.SetOperation("index"); built = await index.BuildAsync(video.Segments, ct); }, ct);
        ct.ThrowIfCancellationRequested();
        video.Complete(video.Segments, topics!.Chapters, topics.Mode);
        if (built!.Vectors.Count > 0) video.SetIndex(built.Vectors, built.Fingerprint);
    }
    private async Task StageAsync(Video video, string key, string label, int progress, Func<Task> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); video.Report(label, progress); video.Step(key, label, "running", clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await action(); ct.ThrowIfCancellationRequested();
        video.Step(key, label, video.Status == VideoStatus.AwaitingTranscript ? "waiting" : "done", clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }
}
