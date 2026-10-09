namespace VideoAtlas.Domain;

public sealed record VideoTerm(string Term, string Description);
public sealed record VideoContext(IReadOnlyList<VideoTerm> Terms, string Instructions)
{
    public static VideoContext Empty { get; } = new([], "");
}
public sealed record TranscriptIssue(Guid Id, int SegmentId, string Reason, string? Suggestion,
    string Source, string Status = "pending", double? SuggestedStart = null, double? SuggestedEnd = null);
public sealed record TranscriptRevision(int Number, DateTimeOffset CreatedAt, string Reason,
    IReadOnlyList<TranscriptSegment> Segments);
