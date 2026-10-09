using System.Text.RegularExpressions;
using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public static partial class TranscriptSearch
{
    private static readonly HashSet<string> StopWords = new("ve veya ile için bir bu şu o ne nasıl neden mı mi mu mü video videoda hakkında bana the a an is are what how in of to and".Split(' '));
    [GeneratedRegex(@"[\p{L}\p{N}]{2,}", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Words();
    public static HashSet<string> Tokens(string text) => Words().Matches(text.ToLowerInvariant())
        .Select(x => x.Value).Where(x => !StopWords.Contains(x)).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<TranscriptSegment> Retrieve(IReadOnlyList<TranscriptSegment> segments, string question)
    {
        var tokens = Tokens(question);
        var ranked = segments.Select(s => new { Segment = s, Score = Tokens(s.Text).Count(w => tokens.Any(t => w == t || (t.Length >= 4 && w.StartsWith(t, StringComparison.Ordinal)))) })
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Segment.Start).Take(24).ToArray();
        return ranked.Select(x => x.Segment).OrderBy(s => s.Start).ToArray();
    }

    public static IReadOnlyList<TranscriptSegment> Context(IReadOnlyList<TranscriptSegment> segments, string question)
    {
        // Small transcripts are sent in full. For long videos use relevant evidence plus evenly spaced coverage.
        if (segments.Sum(s => s.Text.Length) <= 48_000) return segments;
        var selected = Retrieve(segments, question).ToList();
        var stride = Math.Max(1, segments.Count / 32);
        selected.AddRange(segments.Where((_, i) => i % stride == 0).Take(32));
        var budget = 0;
        return selected.DistinctBy(s => s.Id).OrderBy(s => s.Start)
            .TakeWhile(s => (budget += s.Text.Length) <= 48_000).ToArray();
    }

    public static IReadOnlyList<Chapter> LocalChapters(IReadOnlyList<TranscriptSegment> segments)
    {
        var chapters = new List<Chapter>();
        var group = new List<TranscriptSegment>();
        foreach (var segment in segments)
        {
            var overlap = group.Count == 0 ? 1 : Tokens(string.Join(' ', group.TakeLast(3).Select(s => s.Text))).Intersect(Tokens(segment.Text)).Count();
            if (group.Count > 0 && ((segment.Start - group[0].Start >= 45 && overlap == 0) || segment.Start - group[0].Start >= 180))
            { chapters.Add(CreateChapter(group)); group.Clear(); }
            group.Add(segment);
        }
        if (group.Count > 0) chapters.Add(CreateChapter(group));
        return chapters;
    }

    private static Chapter CreateChapter(List<TranscriptSegment> group)
    {
        var words = group.SelectMany(s => Tokens(s.Text)).GroupBy(w => w)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(3).Select(g => g.Key);
        var title = string.Join(" · ", words);
        var summary = group[0].Text.Length > 220 ? group[0].Text[..220] + "…" : group[0].Text;
        return new(title.Length > 0 ? title : "Konuşma", summary, "Anahtar kelimeler", group[0].Start, group[^1].End);
    }
}
