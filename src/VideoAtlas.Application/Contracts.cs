using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public sealed record VideoSummary(Guid Id, string Title, DateTimeOffset CreatedAt, VideoStatus Status,
    string Stage, int Progress, double Duration, bool MediaReady, string? ErrorCode);
public sealed record VideoDetail(VideoSummary Video, IReadOnlyList<TranscriptSegment> Segments,
    IReadOnlyList<Chapter> Chapters, string AnalysisMode, bool Shared, DateTimeOffset? ShareExpiresAt, string IndexStatus = "none", bool AiConfigured = false,
    int TranscriptVersion = 0, IReadOnlyList<int>? UncertainSegmentIds = null, SharePresentation? Presentation = null, int ReviewVersion = 0,
    bool SpeakerDiarization = false, int? ExpectedSpeakerCount = null, IReadOnlyList<SpeakerView>? Speakers = null,
    bool SpeakerDiarizationAvailable = false);
public sealed record SpeakerView(string Id, string Name);
public sealed record Citation(int SegmentId, double Start, double End, string Text);
public sealed record ChatTurn(string Role, string Content);
public sealed record ChatRequest(string Question, IReadOnlyList<ChatTurn>? History = null, string Action = "question", double? Start = null, double? End = null);
public sealed record GroundedClaim(string Text, IReadOnlyList<Citation> Citations);
public sealed record AiClaim(string Text, IReadOnlyList<int> SegmentIds);
public sealed record ChatAnswer(string Answer, IReadOnlyList<Citation> Citations, string Mode, string? Warning = null, int TranscriptVersion = 0,
    IReadOnlyList<GroundedClaim>? Claims = null);
public sealed record TopicResult(IReadOnlyList<Chapter> Chapters, string Mode);
public sealed record AiAnswer(string Answer, IReadOnlyList<int> SegmentIds, IReadOnlyList<AiClaim>? Claims = null, string? Uncertainty = null);

public interface IVideoStore
{
    Task<Video?> FindAsync(Guid id, CancellationToken ct);
    Task<Video?> ReloadAsync(Guid id, CancellationToken ct) => FindAsync(id, ct);
    Task<Video?> FindSharedAsync(string hash, CancellationToken ct);
    Task<IReadOnlyList<VideoSummary>> ListAsync(string ownerId, int page, CancellationToken ct);
    Task AddAsync(Video video, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}
public interface ILibraryStore
{
    Task<long> UsedBytesAsync(string ownerId, CancellationToken ct);
    Task DeleteAsync(Video video, CancellationToken ct);
    Task<bool> ReserveChatAsync(Guid id, int limit, CancellationToken ct);
}
public interface IAiUsageContext
{
    void SetVideo(Video video);
    void SetOperation(string operation, double? audioSeconds = null);
}
public interface IMediaStorage
{
    Task SaveUploadAsync(Guid id, Stream content, CancellationToken ct);
    string PathFor(Guid id, string name);
    void Delete(Guid id);
}
public interface IVideoIntelligence
{
    Task<bool> IsConfiguredAsync(CancellationToken ct);
    Task<bool> SpeechConfiguredAsync(CancellationToken ct) => Task.FromResult(false);
    Task<bool> SpeakerDiarizationConfiguredAsync(CancellationToken ct) => Task.FromResult(false);
    Task<string> IndexStatusAsync(Video video, CancellationToken ct) => Task.FromResult("none");
    Task<TopicResult> CategorizeAsync(IReadOnlyList<TranscriptSegment> segments, CancellationToken ct);
    Task<TopicResult> CategorizeAsync(IReadOnlyList<TranscriptSegment> segments, VideoContext context, CancellationToken ct) => CategorizeAsync(segments, ct);
    Task<AiAnswer> AnswerAsync(IReadOnlyList<TranscriptSegment> evidence, ChatRequest request, CancellationToken ct);
    Task<AiAnswer> AnswerAsync(IReadOnlyList<TranscriptSegment> evidence, ChatRequest request, VideoContext context, CancellationToken ct) => AnswerAsync(evidence, request, ct);
    Task<IReadOnlyList<TranscriptSegment>> EvidenceAsync(Video video, ChatRequest request, CancellationToken ct) =>
        Task.FromResult(TranscriptSearch.Context(video.Segments, request.Question));
}
public sealed class AppException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public static class VideoMapping
{
    public static VideoSummary Summary(Video v) => new(v.Id, v.Title, v.CreatedAt, v.Status,
        v.Stage, v.Progress, v.Duration, v.MediaReady, v.ErrorCode);
    public static VideoDetail Detail(Video v, DateTimeOffset now) => new(Summary(v), v.Segments,
        v.Chapters, v.AnalysisMode, v.CanShare(now), v.ShareExpiresAt, TranscriptVersion: v.TranscriptVersion,
        UncertainSegmentIds: v.TranscriptIssues.Where(i => i.Status == "pending").Select(i => i.SegmentId).Distinct().ToArray(), ReviewVersion: v.ReviewVersion,
        SpeakerDiarization: v.SpeakerDiarizationEnabled, ExpectedSpeakerCount: v.ExpectedSpeakerCount,
        Speakers: v.Segments.Select(segment => segment.Speaker).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct()
            .Select(id => new SpeakerView(id!, v.SpeakerNames.GetValueOrDefault(id!) ?? id!.Replace('_', ' '))).ToArray());
}
