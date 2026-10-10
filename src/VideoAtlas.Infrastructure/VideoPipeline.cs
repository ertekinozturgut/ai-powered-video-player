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
            connection.Model, JsonSerializer.Serialize(context.Terms), context.DiarizationEnabled, context.SpeakerCount,
            "speech-windows-v6-temporal-acoustic-diarization"))));

    public async Task RunAsync(Video video, CancellationToken ct)
    {
        usage.SetVideo(video);
        await StageAsync(video, "media", "Medya ve ses hazırlanıyor", 5, async () =>
        {
            var prepared = await media.PrepareAsync(video.Id, ct);
            video.SetMedia(prepared.Duration, prepared.HasVideo);
        }, ct);
        if (video.Segments.Count == 0)
        {
            var route = await providers.SpeechAsync(ct);
            if (route is null) { video.AwaitTranscript(); video.Step("speech", "Konuşmayı metne çevirme", "waiting", clock.GetUtcNow()); return; }
            if (video.Context.DiarizationEnabled && !route.Adapter.SupportsSpeakerDiarization)
                throw new AppException(409, "diarization_unsupported", "Aktif STT sağlayıcısı konuşmacı ayrımını desteklemiyor. ElevenLabs veya uyumlu bir n8n STT akışı seçin.");
            var fingerprint = SpeechFingerprint(route.Connection, video.Context);
            if (video.TranscriptChunks.Any(c => c.Fingerprint != fingerprint))
                throw new AppException(409, "stt_checkpoint_mismatch", "Tamamlanan ses parçaları farklı STT/model/sözlüğe ait. Eski ayara dönün veya ses kontrol noktalarını açıkça sıfırlayın.");
            await StageAsync(video, "speech", "Konuşmayı metne çevirme", 15, async () =>
            {
                var windows = video.Context.DiarizationEnabled
                    ? new[] { new SilenceInterval(0, video.Duration) }
                    : await media.TranscriptionWindowsAsync(video.Id, video.Duration, ct);
                foreach (var window in windows)
                {
                    ct.ThrowIfCancellationRequested();
                    var offset = window.Start;
                    if (video.TranscriptChunks.Any(c => c.Offset == offset)) continue;
                    var end = window.End;
                    var audio = await media.AudioAsync(video.Id, offset, end - offset, ct);
                    if (audio is null) { video.AwaitTranscript(); return; }
                    video.Report($"Konuşma parçası {video.TranscriptChunks.Count + 1}/{windows.Count} çözümleniyor", 15 + (int)(45 * offset / video.Duration));
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
                    IReadOnlyList<TranscriptIssue> issues;
                    try { issues = await reviewer.InspectBatchAsync(context, video.Context, ct); }
                    catch (AiException exception) when (exception.Code == "ai_invalid_response")
                    {
                        // A malformed optional review response must not invalidate a
                        // successfully produced transcript. Keep deterministic local
                        // findings and continue with the next batch.
                        issues = [];
                    }
                    var scoped = issues.Where(i => batch.Any(s => s.Id == i.SegmentId)).ToArray();
                    video.AdvanceQuality(video.QualityCursor + batch.Length, scoped);
                    video.Report($"Kalite incelemesi: {video.QualityCursor}/{video.Segments.Count} konuşma", 65 + (int)(12d * video.QualityCursor / video.Segments.Count));
                    await db.SaveChangesAsync(ct);
                }
            }
        }, ct);
        TopicResult? topics = null;
        await StageAsync(video, "topics", "Bölüm özetleri ve konu başlıkları", 80, async () =>
        {
            usage.SetOperation("topics");
            try { topics = await ai.CategorizeAsync(video.Segments, video.Context, ct); }
            catch (AiException exception) when (exception.Code == "ai_invalid_response")
            {
                // Some gateway models return syntactically valid JSON with invented or
                // non-contiguous segment IDs. Keep the video usable with deterministic,
                // time-coded local chapters instead of failing the whole analysis.
                topics = new(TranscriptSearch.LocalChapters(video.Segments), "keywords");
                video.Report("AI konu yanıtı geçersiz; yerel konu başlıkları hazırlandı", 80);
            }
        }, ct);
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
