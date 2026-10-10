using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Mcp;

public sealed class TopicSegmenter
{
    public IReadOnlyList<Chapter> Split(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0) return [];
        var chapters = new List<Chapter>();
        var group = new List<TranscriptSegment>();
        foreach (var segment in segments.OrderBy(item => item.Start))
        {
            var duration = group.Count == 0 ? 0 : segment.Start - group[0].Start;
            var previousContext = TranscriptSearch.Tokens(string.Join(' ', group.TakeLast(4).Select(item => item.Text)));
            var current = TranscriptSearch.Tokens(segment.Text);
            var similarity = Jaccard(previousContext, current);
            if (group.Count > 0 && (duration >= 180 || (duration >= 35 && similarity < 0.06)))
            {
                chapters.Add(Create(group));
                group.Clear();
            }
            group.Add(segment);
        }
        if (group.Count > 0) chapters.Add(Create(group));
        return chapters;
    }

    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var intersection = left.Intersect(right).Count();
        return intersection / (double)(left.Count + right.Count - intersection);
    }

    private static Chapter Create(IReadOnlyList<TranscriptSegment> segments)
    {
        var keywords = segments.SelectMany(item => TranscriptSearch.Tokens(item.Text))
            .GroupBy(word => word).OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal).Take(4).Select(group => group.Key).ToArray();
        var title = keywords.Length == 0 ? "Conversation" : string.Join(" · ", keywords);
        var text = string.Join(' ', segments.Take(3).Select(item => item.Text));
        var summary = text.Length <= 360 ? text : text[..360] + "…";
        return new(title, summary, "Local contextual similarity", segments[0].Start, segments[^1].End);
    }
}
