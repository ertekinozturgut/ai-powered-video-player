using VideoAtlas.Domain;

namespace VideoAtlas.Mcp;

public sealed record AudioAnalysisManifest(
    string Source,
    string OutputDirectory,
    string Model,
    string? Language,
    int? SpeakerCount,
    bool AutomaticSpeakerDetection,
    string TranscriptJson,
    string Srt,
    string TopicsJson,
    int SegmentCount,
    int TopicCount,
    double DurationSeconds);

public sealed record TranscriptDocument(
    string Source,
    string Model,
    string? Language,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TranscriptSegment> Segments);

public sealed record TopicDocument(
    string Mode,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Chapter> Topics);

public sealed record DependencyStatus(
    bool Ready,
    IReadOnlyDictionary<string, string> Commands,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> AllowedRoots);
