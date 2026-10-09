using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public sealed class VideoChat(IVideoIntelligence intelligence, ILibraryStore library, IAiUsageContext? usage = null)
{
    public async Task<ChatAnswer> AskAsync(Video video, ChatRequest request, CancellationToken ct)
    {
        VideoAccess.RequireReady(video);
        usage?.SetVideo(video); usage?.SetOperation("chat");
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 2000 ||
            request.Action is not ("question" or "summary" or "decisions" or "actions" or "section") ||
            request.History?.Count > 10 || request.History?.Any(h => h is null || h.Content is null || h.Content.Length > 4000 || h.Role is not ("user" or "assistant")) == true)
            throw new AppException(400, "invalid_question", "Soruyu 1–2000 karakter arasında yazın.");
        if (request.Action == "section" && (request.Start is not { } start || request.End is not { } end ||
            !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > video.Duration + 1))
            throw new AppException(400, "invalid_section", "Açıklanacak bölüm video süresi içinde olmalıdır.");
        if (!await library.ReserveChatAsync(video.Id, 200, ct))
            throw new AppException(429, "chat_quota", "Bu videonun günlük 200 soru sınırına ulaşıldı.");
        if (!await intelligence.IsConfiguredAsync(ct)) return Warn(video, Extract(video, request));
        var context = request.Action == "section"
            ? TranscriptSearch.Context(video.Segments.Where(s => s.Start < request.End && s.End > request.Start).ToArray(), request.Question)
            : request.Action is "summary" or "decisions" or "actions" ? Overview(video) : await intelligence.EvidenceAsync(video, request, ct);
        var answer = await intelligence.AnswerAsync(context, request, video.Context, ct);
        if (answer.Claims is not null)
        {
            if (answer.Claims.Count is 0 or > 8 || answer.Claims.Any(c => string.IsNullOrWhiteSpace(c.Text) || c.Text.Length > 4000 || c.SegmentIds.Count is 0 or > 8 || c.SegmentIds.Any(id => !context.Any(s => s.Id == id))))
                return new("Bu yanıtın iddialarını video kaynaklarıyla doğrulayamadım. Soruyu daraltarak tekrar deneyin.", [], "ai", TranscriptVersion: video.TranscriptVersion);
            var claims = answer.Claims.Select(c => new GroundedClaim(c.Text, context.Where(s => c.SegmentIds.Contains(s.Id)).Select(Cite).ToArray())).ToArray();
            return Warn(video, new(string.Join("\n\n", claims.Select(c => c.Text)), claims.SelectMany(c => c.Citations).DistinctBy(c => c.SegmentId).ToArray(), "ai",
                Warning: answer.Uncertainty, Claims: claims));
        }
        var ids = answer.SegmentIds.Distinct().Take(8).ToArray();
        var citations = context.Where(s => ids.Contains(s.Id)).Select(Cite).ToArray();
        // Invalid or absent citations can never turn into an ungrounded affirmative answer.
        if (citations.Length == 0 || ids.Any(id => !context.Any(s => s.Id == id)))
            return new("Bu soruyu yanıtlamak için video dökümünde yeterli kanıt bulamadım.", [], "ai", TranscriptVersion: video.TranscriptVersion);
        return Warn(video, new(answer.Answer, citations, "ai", Warning: answer.Uncertainty));
    }
    private static ChatAnswer Warn(Video video, ChatAnswer answer) => answer with
    {
        TranscriptVersion = video.TranscriptVersion,
        Warning = string.Join(" ", new[] { answer.Warning, video.TranscriptIssues.Any(i => i.Status == "pending" && answer.Citations.Any(c => c.SegmentId == i.SegmentId))
            ? "Bu cevabın kaynaklarında incelenmesi gereken transkript bölümleri var. İlgili sesi dinleyerek doğrulayın." : null }.Where(s => !string.IsNullOrWhiteSpace(s)))
    };
    private static IReadOnlyList<TranscriptSegment> Overview(Video video)
    {
        if (video.Segments.Sum(s => s.Text.Length) <= 48000) return video.Segments;
        // Give every temporal region a budget; early sections cannot consume the whole context.
        var groups = video.Segments.GroupBy(s => (int)(s.Start / Math.Max(1, video.Duration / 24))).ToArray();
        var result = new List<TranscriptSegment>();
        foreach (var group in groups)
        {
            var used = 0;
            foreach (var segment in group)
                if (used + segment.Text.Length <= 48000 / groups.Length) { result.Add(segment); used += segment.Text.Length; }
        }
        return result.OrderBy(s => s.Start).ToArray();
    }
    private static Citation Cite(TranscriptSegment s) => new(s.Id, s.Start, s.End, s.Text);
    private static ChatAnswer Extract(Video video, ChatRequest request)
    {
        var segments = request.Action == "section" ? video.Segments.Where(s => s.Start < request.End && s.End > request.Start).ToArray() : video.Segments.ToArray();
        var matches = TranscriptSearch.Retrieve(segments, request.Question).Take(4).ToArray();
        return matches.Length == 0
            ? new("Bu sözcüklerle eşleşen bir konuşma bulamadım. Videoda geçen farklı sözcükler deneyin. AI bağlantısı yapılandırılmadığı için yalnızca alıntı araması yapılır.", [], "extractive")
            : new("Dökümdeki ilgili alıntılar:\n\n" + string.Join("\n\n", matches.Select(s => s.Text)), matches.Select(Cite).ToArray(), "extractive");
    }
}
