using System.Text.RegularExpressions;
using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public sealed record ReviewState(int Version, VideoContext Context, IReadOnlyList<TranscriptIssue> Issues,
    IReadOnlyList<RevisionInfo> Revisions, bool HasOriginal, IReadOnlyList<TranscriptSegment> OriginalSegments,
    IReadOnlyList<TranscriptSegment> Segments, int TranscriptVersion);
public sealed record RevisionInfo(int Number, DateTimeOffset CreatedAt, string Reason);
public sealed record ContextCommand(int Version, IReadOnlyList<VideoTerm> Terms, string Instructions);
public sealed record EditTranscriptCommand(int Version, int SegmentId, string Text, bool AudioVerified, double? Start = null, double? End = null);
public sealed record EditChapterCommand(int Version, int ChapterIndex, string Title, string Summary, string Category, double Start, double End);
public sealed record ReviewCommand(int Version);
public interface ITranscriptReviewer
{
    Task<IReadOnlyList<TranscriptIssue>> InspectBatchAsync(IReadOnlyList<TranscriptSegment> segments, VideoContext context, CancellationToken ct) => Task.FromResult<IReadOnlyList<TranscriptIssue>>([]);
    Task<TranscriptIssue?> InspectAsync(IReadOnlyList<TranscriptSegment> neighbors, int segmentId, VideoContext context, CancellationToken ct);
    Task<TranscriptIssue> RetranscribeAsync(Guid videoId, double duration, TranscriptSegment segment, CancellationToken ct);
    Task<TranscriptIssue> RetranscribeAsync(Guid videoId, double duration, TranscriptSegment segment, VideoContext context, bool secondary, CancellationToken ct) => RetranscribeAsync(videoId, duration, segment, ct);
    Task<TranscriptIssue> AlignAsync(Guid videoId, double duration, TranscriptSegment segment, CancellationToken ct) => throw new AppException(409, "alignment_unavailable", "Aktif STT sağlayıcısı ses–metin hizalamasını desteklemiyor.");
}

public static partial class TranscriptQuality
{
    public static IReadOnlyList<TranscriptIssue> SilenceIssues(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<SilenceInterval> silence) =>
        segments.Where(s => s.End > s.Start && silence.Sum(g => Math.Max(0, Math.Min(g.End, s.End) - Math.Max(g.Start, s.Start))) / (s.End - s.Start) >= .8)
            .Take(300).Select(s => new TranscriptIssue(Guid.NewGuid(), s.Id,
                "Bu metnin zaman aralığının en az %80'i düşük ses seviyesinde (-40 dB). Sessizlikte üretilmiş metin veya çok kısık konuşma olabilir; sesi doğrulayın.", null, "silence")).ToArray();
    public static VideoContext ValidateContext(IReadOnlyList<VideoTerm>? terms, string? instructions)
    {
        if (terms is null || terms.Count > 50 || instructions is null || instructions.Length > 4000 ||
            terms.Any(t => t is null || string.IsNullOrWhiteSpace(t.Term) || t.Term.Length > 100 ||
                t.Term.IndexOfAny(['|', '\r', '\n']) >= 0 || string.IsNullOrWhiteSpace(t.Description) || t.Description.Length > 500 || t.Description.IndexOfAny(['\r', '\n']) >= 0))
            throw new AppException(400, "invalid_context", "En fazla 50 terim; terim 100, açıklama 500, yönerge 4000 karakter olabilir.");
        var clean = terms.Select(t => new VideoTerm(t.Term.Trim(), t.Description.Trim())).ToArray();
        if (clean.Select(t => t.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count() != clean.Length)
            throw new AppException(400, "invalid_context", "Aynı terimi yalnızca bir kez ekleyin.");
        return new(clean, instructions.Trim());
    }
    public static IReadOnlyList<TranscriptIssue> Detect(IReadOnlyList<TranscriptSegment> segments, VideoContext context)
    {
        var issues = new List<TranscriptIssue>();
        for (var n = 0; n < segments.Count && issues.Count < 300; n++)
        {
            var s = segments[n];
            var reasons = new List<string>();
            if (s.End <= s.Start || s.Text.Length / Math.Max(.1, s.End - s.Start) > 28)
                reasons.Add("Süreye göre yoğun metin; ses ve zaman aralığını kontrol edin.");
            if (n > 0 && string.Equals(s.Text.Trim(), segments[n - 1].Text.Trim(), StringComparison.OrdinalIgnoreCase))
                reasons.Add("Ardışık aynı metin; gerçek tekrar veya STT tekrarı olabilir.");
            if (n > 0 && s.Start < segments[n - 1].End)
                reasons.Add("Konuşma zamanları örtüşüyor; sesi dinleyerek kontrol edin.");
            var tokens = Words().Matches(s.Text).Select(m => m.Value.ToLowerInvariant()).Distinct().Take(500).ToArray();
            foreach (var term in context.Terms.Where(t => t.Term.Length is >= 4 and <= 60 && !t.Term.Contains(' ')))
                if (tokens.Any(t => t != term.Term.ToLowerInvariant() && OneEditApart(t, term.Term.ToLowerInvariant())))
                    reasons.Add($"“{term.Term}” terimine benzeyen bir yazım var; otomatik değiştirilmedi.");
            if (reasons.Count > 0) issues.Add(new(Guid.NewGuid(), s.Id, string.Join(" ", reasons).Truncate(2000), null, "heuristic"));
        }
        return issues;
    }
    private static bool OneEditApart(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1) return false;
        var i = 0; var j = 0; var edits = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j]) { i++; j++; continue; }
            if (++edits > 1) return false;
            if (a.Length >= b.Length) i++;
            if (b.Length >= a.Length) j++;
        }
        return edits + (a.Length - i) + (b.Length - j) == 1;
    }
    private static string Truncate(this string text, int limit) => text.Length <= limit ? text : text[..limit];
    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Words();
}

public sealed class TranscriptReviewService(IVideoStore store, MutationGate gate,
    ITranscriptReviewer reviewer, TimeProvider clock, IAiUsageContext? usage = null)
{
    public static ReviewState State(Video v) => new(v.ReviewVersion, v.Context, v.TranscriptIssues,
        v.TranscriptHistory.Select(r => new RevisionInfo(r.Number, r.CreatedAt, r.Reason)).ToArray(), v.OriginalSegments.Count > 0, v.OriginalSegments, v.Segments, v.TranscriptVersion);

    public Task SaveContextAsync(Guid id, string owner, ContextCommand command, CancellationToken ct) =>
        MutateAsync(id, owner, command.Version, v =>
        {
            v.SetContext(TranscriptQuality.ValidateContext(command.Terms, command.Instructions));
            v.SetIssues(TranscriptQuality.Detect(v.Segments, v.Context));
            if (v.Segments.Count > 0) { v.SetIndex([], ""); v.Requeue(); }
        }, ct);

    public Task CheckAsync(Guid id, string owner, int version, CancellationToken ct) =>
        MutateAsync(id, owner, version, v => v.SetIssues(TranscriptQuality.Detect(v.Segments, v.Context)), ct);

    public Task EditAsync(Guid id, string owner, EditTranscriptCommand command, CancellationToken ct) =>
        MutateAsync(id, owner, command.Version, v =>
        {
            if (!command.AudioVerified) throw new AppException(400, "audio_confirmation", "Kaydetmeden önce ilgili sesi dinleyip metni doğrulayın.");
            if (string.IsNullOrWhiteSpace(command.Text) || command.Text.Length > 16000)
                throw new AppException(400, "invalid_transcript_text", "Metin 1–16000 karakter olmalıdır.");
            var target = v.Segments.FirstOrDefault(s => s.Id == command.SegmentId) ?? throw VideoAccess.Missing();
            var start = command.Start ?? target.Start; var end = command.End ?? target.End;
            if (command.Start.HasValue != command.End.HasValue || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > v.Duration + 1 ||
                v.Segments.Any(s => s.Id < target.Id && s.Start > start || s.Id > target.Id && s.Start < start))
                throw new AppException(400, "invalid_timing", "Başlangıç/bitiş video içinde ve konuşma sırasına uygun olmalıdır.");
            var clean = string.Join('\n', command.Text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))).Trim();
            if (clean.Any(c => char.IsControl(c) && c is not ('\n' or '\t')))
                throw new AppException(400, "invalid_transcript_text", "Metinde desteklenmeyen kontrol karakterleri var.");
            var segments = v.Segments.Select(s => s.Id == command.SegmentId ? s with { Text = clean, Start = start, End = end } : s).ToArray();
            var unaffected = v.TranscriptIssues.Where(i => i.SegmentId != command.SegmentId).ToArray();
            v.RecordTranscript(segments, clock.GetUtcNow(), "Ses doğrulamalı kullanıcı düzeltmesi");
            v.SetIssues(unaffected.Concat(TranscriptQuality.Detect(v.Segments, v.Context).Where(i => i.SegmentId == command.SegmentId)));
            v.Requeue();
        }, ct);

    public Task EditChapterAsync(Guid id, string owner, EditChapterCommand command, CancellationToken ct) =>
        MutateAsync(id, owner, command.Version, v =>
        {
            if (command.ChapterIndex < 0 || command.ChapterIndex >= v.Chapters.Count) throw VideoAccess.Missing();
            if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 240 || command.Summary?.Length > 4000 ||
                string.IsNullOrWhiteSpace(command.Category) || command.Category.Length > 120 || !double.IsFinite(command.Start) ||
                !double.IsFinite(command.End) || command.Start < 0 || command.End <= command.Start || command.End > v.Duration + 1)
                throw new AppException(400, "invalid_chapter", "Başlık, kategori ve zaman aralığını kontrol edin.");
            var fields = new[] { command.Title, command.Summary ?? "", command.Category };
            if (fields.Any(value => value.Any(char.IsControl)))
                throw new AppException(400, "invalid_chapter", "Konu başlığında desteklenmeyen kontrol karakterleri var.");
            v.UpdateChapter(command.ChapterIndex, new Chapter(command.Title.Trim(), command.Summary?.Trim() ?? "", command.Category.Trim(), command.Start, command.End));
        }, ct);

    public Task DismissAsync(Guid id, string owner, Guid issueId, int version, CancellationToken ct) =>
        MutateAsync(id, owner, version, v =>
        {
            if (!v.TranscriptIssues.Any(i => i.Id == issueId)) throw VideoAccess.Missing();
            v.ResolveIssue(issueId);
        }, ct);

    public Task RestoreAsync(Guid id, string owner, int number, int version, CancellationToken ct) =>
        MutateAsync(id, owner, version, v =>
        {
            var segments = number == -1 ? v.OriginalSegments : v.TranscriptHistory.FirstOrDefault(r => r.Number == number)?.Segments;
            if (segments is null || segments.Count == 0) throw VideoAccess.Missing();
            v.RecordTranscript(segments, clock.GetUtcNow(), number == -1 ? "Ham metne dönüş" : $"Sürüm {number} geri yüklendi");
            v.SetIssues(TranscriptQuality.Detect(v.Segments, v.Context));
            v.Requeue();
        }, ct);

    public async Task ProposeAsync(Guid id, string owner, int segmentId, int version, bool retranscribe, CancellationToken ct, bool secondary = false, bool align = false)
    {
        TranscriptSegment segment;
        TranscriptSegment[] neighbors;
        VideoContext context;
        double duration;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var v = await CurrentAsync(id, owner, version, ct);
            segment = v.Segments.FirstOrDefault(s => s.Id == segmentId) ?? throw VideoAccess.Missing();
            var position = v.Segments.FindIndex(s => s.Id == segmentId);
            neighbors = v.Segments.Skip(Math.Max(0, position - 2)).Take(5).ToArray();
            context = v.Context; duration = v.Duration;
            usage?.SetVideo(v);
        }
        finally { gate.Semaphore.Release(); }
        // No global mutation lock is held during paid network/audio operations.
        usage?.SetOperation(align ? "alignment" : secondary ? "secondary-stt" : retranscribe ? "retranscribe" : "review",
            retranscribe || align ? Math.Min(duration, segment.End + 3) - Math.Max(0, segment.Start - 3) : null);
        var issue = align ? await reviewer.AlignAsync(id, duration, segment, ct)
            : retranscribe ? await reviewer.RetranscribeAsync(id, duration, segment, context, secondary, ct)
            : await reviewer.InspectAsync(neighbors, segmentId, context, ct);
        await MutateAsync(id, owner, version, v =>
        {
            var source = align ? "alignment" : secondary ? "stt-secondary" : retranscribe ? "stt" : "llm";
            var retained = v.TranscriptIssues.Where(i => i.SegmentId != segmentId || i.Source != source).ToList();
            if (issue is not null) retained.Insert(0, issue);
            v.SetIssues(retained);
        }, ct);
    }
    private async Task<Video> CurrentAsync(Guid id, string owner, int version, CancellationToken ct)
    {
        var current = await store.ReloadAsync(id, ct);
        if (current is null || current.OwnerId != owner) throw VideoAccess.Missing();
        VideoAccess.RequireIdle(current);
        if (current.ReviewVersion != version) throw new AppException(409, "review_conflict", "Metin veya ayarlar değişti. Sayfayı yenileyip tekrar deneyin; eski öneri uygulanmadı.");
        return current;
    }
    private async Task MutateAsync(Guid id, string owner, int version, Action<Video> action, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try { var v = await CurrentAsync(id, owner, version, ct); action(v); await store.SaveAsync(ct); }
        finally { gate.Semaphore.Release(); }
    }
}
