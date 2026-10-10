namespace VideoAtlas.Domain;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SensitiveDataAttribute : Attribute;

public enum VideoStatus { Queued, Processing, AwaitingTranscript, Ready, Failed, Canceled }
public sealed record TranscriptSegment(int Id, double Start, double End, [property: SensitiveData] string Text);
public sealed record Chapter(string Title, string Summary, string Category, double Start, double End);
public sealed record TranscriptVector(int[] SegmentIds, float[] Values);

public sealed partial class Video
{
    private Video() { }
    public Video(Guid id, string ownerId, string title, long sourceBytes, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        Title = title;
        SourceBytes = sourceBytes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = "";
    [SensitiveData] public string Title { get; private set; } = "";
    public long SourceBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public VideoStatus Status { get; private set; } = VideoStatus.Queued;
    public string Stage { get; private set; } = "İşleme sırası bekleniyor";
    public int Progress { get; private set; }
    public double Duration { get; private set; }
    public bool MediaReady { get; private set; }
    public string? ErrorCode { get; private set; }
    public string AnalysisMode { get; private set; } = "none";
    [SensitiveData] public List<TranscriptSegment> Segments { get; private set; } = [];
    public List<Chapter> Chapters { get; private set; } = [];
    public List<TranscriptVector> Vectors { get; private set; } = [];
    public string? EmbeddingModel { get; private set; }
    public string? ShareHash { get; private set; }
    public DateTimeOffset? ShareExpiresAt { get; private set; }
    public string FrameOrigins { get; private set; } = "'none'";
    public int DailyChatCount { get; private set; }
    public string ChatDay { get; private set; } = "";
    [SensitiveData] public List<VideoTerm> Terms { get; private set; } = [];
    [SensitiveData] public string AnalysisInstructions { get; private set; } = "";
    public string AnalysisLanguage { get; private set; } = "tr";
    [SensitiveData] public List<TranscriptSegment> OriginalSegments { get; private set; } = [];
    [SensitiveData] public List<TranscriptRevision> TranscriptHistory { get; private set; } = [];
    [SensitiveData] public List<TranscriptIssue> TranscriptIssues { get; private set; } = [];
    public int TranscriptVersion { get; private set; }
    public int ReviewVersion { get; private set; }
    public VideoContext Context => new(Terms, AnalysisInstructions, AnalysisLanguage);

    public void SetContext(VideoContext context)
    {
        Terms = context.Terms.ToList(); AnalysisInstructions = context.Instructions; AnalysisLanguage = context.Language;
        TranscriptIssues = []; ReviewVersion++;
        ResetQualityProgress();
    }
    public void RecordTranscript(IEnumerable<TranscriptSegment> segments, DateTimeOffset now, string reason)
    {
        var next = segments.ToList();
        if (OriginalSegments.Count == 0) OriginalSegments = (Segments.Count > 0 ? Segments : next).ToList();
        if (TranscriptHistory.Count == 0 && Segments.Count > 0)
            TranscriptHistory.Add(new(TranscriptVersion, now, "Önceki metin", Segments.ToArray()));
        Segments = next; TranscriptVersion++; ReviewVersion++;
        TranscriptHistory.Add(new(TranscriptVersion, now, reason, next.ToArray()));
        TranscriptHistory = TranscriptHistory.TakeLast(10).ToList();
        TranscriptIssues = []; Chapters = []; Vectors = []; EmbeddingModel = null;
        ResetQualityProgress();
    }
    public int ApplyAiSuggestions(IEnumerable<TranscriptIssue> issues, DateTimeOffset now)
    {
        var replacements = issues.Where(issue => issue.Source == "llm-batch" && !string.IsNullOrWhiteSpace(issue.Suggestion))
            .GroupBy(issue => issue.SegmentId).ToDictionary(group => group.Key, group => group.Last().Suggestion!.Trim());
        if (replacements.Count == 0) return 0;
        var changed = 0;
        var next = Segments.Select(segment =>
        {
            if (!replacements.TryGetValue(segment.Id, out var text) || text.Length > 16000 ||
                text.Any(character => char.IsControl(character) && character is not ('\n' or '\t')) ||
                string.Equals(segment.Text.Trim(), text, StringComparison.Ordinal)) return segment;
            changed++;
            return segment with { Text = text };
        }).ToArray();
        if (changed == 0) return 0;
        if (OriginalSegments.Count == 0) OriginalSegments = Segments.ToList();
        Segments = next.ToList(); TranscriptVersion++; ReviewVersion++;
        TranscriptHistory.Add(new(TranscriptVersion, now, $"AI bağlam doğrulamalı otomatik düzeltme ({changed} bölüm)", next));
        TranscriptHistory = TranscriptHistory.TakeLast(10).ToList();
        Chapters = []; Vectors = []; EmbeddingModel = null;
        return changed;
    }
    public void SetIssues(IEnumerable<TranscriptIssue> issues)
    { TranscriptIssues = issues.Take(300).ToList(); ReviewVersion++; }
    public void ResolveIssue(Guid id)
    {
        TranscriptIssues = TranscriptIssues.Select(i => i.Id == id ? i with { Status = "dismissed" } : i).ToList();
        ReviewVersion++;
    }
    public void UpdateChapter(int index, Chapter chapter)
    {
        if (index < 0 || index >= Chapters.Count) throw new InvalidOperationException("Chapter does not exist.");
        Chapters[index] = chapter;
        ReviewVersion++;
    }

    public bool IsBusy => Status is VideoStatus.Queued or VideoStatus.Processing;
    public void Begin() { Status = VideoStatus.Processing; ErrorCode = null; Report("Video hazırlanıyor", 5); }
    public void Report(string stage, int progress) { Stage = stage; Progress = Math.Clamp(progress, 0, 100); }
    public void SetMedia(double duration) { Duration = duration; MediaReady = true; }
    public void AwaitTranscript()
    {
        Status = VideoStatus.AwaitingTranscript;
        Report("SRT yükleyin veya AI yapılandırıldıktan sonra yeniden işleyin", 100);
    }
    public void Complete(IEnumerable<TranscriptSegment> segments, IEnumerable<Chapter> chapters, string mode)
    {
        Segments = segments.ToList();
        if (OriginalSegments.Count == 0 && Segments.Count > 0) OriginalSegments = Segments.ToList();
        Chapters = chapters.ToList();
        Vectors = [];
        EmbeddingModel = null;
        AnalysisMode = mode;
        Status = VideoStatus.Ready;
        ErrorCode = null;
        Report("Analiz tamamlandı", 100);
    }
    public void Fail(string code)
    {
        Status = VideoStatus.Failed;
        ErrorCode = code;
        Report("İşlem tamamlanamadı; ayrıntıyı kontrol edip yeniden deneyin", 0);
    }
    public void Requeue() { Status = VideoStatus.Queued; ErrorCode = null; CancelRequested = false; Report("İşleme sırası bekleniyor", 0); }
    public void Share(string hash, DateTimeOffset? expiresAt, string origins)
    { ShareHash = hash; ShareExpiresAt = expiresAt; FrameOrigins = origins; }
    public void RevokeShare() { ShareHash = null; ShareExpiresAt = null; FrameOrigins = "'none'"; }
    public void SetIndex(IEnumerable<TranscriptVector> vectors, string model) { Vectors = vectors.ToList(); EmbeddingModel = model; }
    public bool CanShare(DateTimeOffset now) => Status == VideoStatus.Ready && ShareHash is not null && (ShareExpiresAt is null || ShareExpiresAt > now);
    public bool ReserveChat(string day, int limit)
    {
        if (ChatDay != day) { ChatDay = day; DailyChatCount = 0; }
        if (DailyChatCount >= limit) return false;
        DailyChatCount++;
        return true;
    }
}
