namespace VideoAtlas.Domain;

public sealed record ProcessingStep(string Key, string Label, string State, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt = null, string? ErrorCode = null);
public sealed record TranscriptChunk(double Offset, double End, string Fingerprint, IReadOnlyList<TranscriptSegment> Segments);
public sealed record SilenceInterval(double Start, double End);
public sealed record SharePresentation(string Theme = "orbital", double StartAt = 0,
    bool ShowTranscript = true, bool ShowChapters = true, bool ChatEnabled = true);

public sealed partial class Video
{
    public bool CancelRequested { get; private set; }
    public int ProcessingAttempts { get; private set; }
    public List<ProcessingStep> ProcessingSteps { get; private set; } = [];
    [SensitiveData] public List<TranscriptChunk> TranscriptChunks { get; private set; } = [];
    public bool FullReviewEnabled { get; private set; }
    public int QualityCursor { get; private set; }
    public bool SilenceChecked { get; private set; }
    public DateTimeOffset? DeleteAfter { get; private set; }
    public string ShareTheme { get; private set; } = "orbital";
    public double ShareStartAt { get; private set; }
    public bool ShareTranscript { get; private set; } = true;
    public bool ShareChapters { get; private set; } = true;
    public bool ShareChat { get; private set; } = true;
    public bool ShareRequiresLogin { get; private set; }
    [SensitiveData] public List<string> ShareViewers { get; private set; } = [];
    public SharePresentation Presentation => new(ShareTheme, ShareStartAt, ShareTranscript, ShareChapters, ShareChat);

    public void StartAttempt() { ProcessingAttempts++; ResetProcessingSteps(); Begin(); }
    public void ResetProcessingSteps() { ProcessingSteps = []; }
    public void Step(string key, string label, string state, DateTimeOffset now, string? error = null)
    {
        var previous = ProcessingSteps.FirstOrDefault(s => s.Key == key);
        var item = state == "running" ? new ProcessingStep(key, label, state, now)
            : new ProcessingStep(key, label, state, previous?.StartedAt ?? now, now, error);
        ProcessingSteps = ProcessingSteps.Where(s => s.Key != key).Append(item).ToList();
    }
    public void Checkpoint(TranscriptChunk chunk)
    { TranscriptChunks = TranscriptChunks.Where(c => c.Offset != chunk.Offset).Append(chunk).OrderBy(c => c.Offset).ToList(); }
    public void ResetChunks() { TranscriptChunks = []; }
    public void RequestCancellation() { CancelRequested = true; }
    public void Cancel()
    { CancelRequested = true; Status = VideoStatus.Canceled; ErrorCode = null; Report("İşlem iptal edildi; tamamlanan ses parçaları korundu", Progress); }
    public void ConfigureFullReview(bool enabled)
    { FullReviewEnabled = enabled; ResetQualityProgress(); ReviewVersion++; }
    public void AdvanceQuality(int cursor, IEnumerable<TranscriptIssue> issues)
    {
        QualityCursor = cursor;
        SetIssues(TranscriptIssues.Concat(issues));
    }
    public void MarkSilenceChecked() { SilenceChecked = true; }
    private void ResetQualityProgress() { QualityCursor = 0; SilenceChecked = false; }
    public void SetRetention(DateTimeOffset? deleteAfter) { DeleteAfter = deleteAfter; ReviewVersion++; }
    public void ConfigureShare(SharePresentation presentation, bool requiresLogin, IEnumerable<string> viewers)
    {
        ShareTheme = presentation.Theme; ShareStartAt = presentation.StartAt;
        ShareTranscript = presentation.ShowTranscript; ShareChapters = presentation.ShowChapters; ShareChat = presentation.ChatEnabled;
        ShareRequiresLogin = requiresLogin; ShareViewers = viewers.ToList();
    }
}
