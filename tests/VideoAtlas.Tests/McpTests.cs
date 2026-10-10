using VideoAtlas.Domain;
using VideoAtlas.Mcp;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class McpTests
{
    [Fact]
    public void Topic_segmenter_splits_context_changes_after_minimum_duration()
    {
        var segments = new[]
        {
            new TranscriptSegment(0, 0, 20, "Whisper transcript model and audio processing pipeline"),
            new TranscriptSegment(1, 20, 40, "Audio model pipeline creates a searchable transcript"),
            new TranscriptSegment(2, 50, 70, "Tomatoes onions and olive oil are added to the recipe"),
            new TranscriptSegment(3, 70, 90, "The recipe is cooked slowly and served warm")
        };

        var topics = new TopicSegmenter().Split(segments);

        Assert.Equal(2, topics.Count);
        Assert.Equal(0, topics[0].Start);
        Assert.Equal(50, topics[1].Start);
    }

    [Fact]
    public void Path_policy_rejects_files_outside_configured_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"videoatlas-mcp-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.mp3");
        var policy = new PathPolicy([root], TimeProvider.System);

        var error = Assert.Throws<UnauthorizedAccessException>(() => policy.ReadFile(outside));

        Assert.Contains("VIDEOATLAS_MCP_ALLOWED_ROOTS", error.Message, StringComparison.Ordinal);
    }
}
