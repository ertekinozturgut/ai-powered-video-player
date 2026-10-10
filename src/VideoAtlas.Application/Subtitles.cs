using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public static partial class Subtitles
{
    [GeneratedRegex(@"^(\d{2,3}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2,3}):(\d{2}):(\d{2})[,.](\d{3})$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Timing();
    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Tags();

    public static List<TranscriptSegment> Parse(string text, double duration)
    {
        if (text.Length is 0 or > 2_000_000) throw Invalid();
        var lines = text.TrimStart('\uFEFF').Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var segments = new List<TranscriptSegment>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            if (int.TryParse(lines[i], out _) && i + 1 < lines.Length) i++;
            var match = Timing().Match(lines[i].Trim());
            if (!match.Success) throw Invalid();
            var start = Seconds(match, 1);
            var end = Seconds(match, 5);
            var body = new StringBuilder();
            while (++i < lines.Length && !string.IsNullOrWhiteSpace(lines[i])) body.AppendLine(lines[i]);
            var clean = Tags().Replace(body.ToString(), "").Trim();
            if (start < 0 || end <= start || end > duration + 1 || clean.Length is 0 or > 4000 ||
                (segments.Count > 0 && start < segments[^1].Start)) throw Invalid();
            segments.Add(new(segments.Count, start, end, clean));
            if (segments.Count > 20_000) throw Invalid();
        }
        if (segments.Count == 0) throw Invalid();
        return segments;
    }

    private static double Seconds(Match m, int offset)
    {
        var values = Enumerable.Range(offset, 4).Select(i => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        if (values[1] > 59 || values[2] > 59) throw Invalid();
        return values[0] * 3600 + values[1] * 60 + values[2] + values[3] / 1000d;
    }
    private static AppException Invalid() => new(400, "invalid_subtitles", "SRT içeriği veya zamanları geçersiz. Zamanlar video süresi içinde ve sıralı olmalıdır.");
    public static string Format(IReadOnlyList<TranscriptSegment> segments, bool webVtt = false,
        IReadOnlyDictionary<string, string>? speakerNames = null)
    {
        var b = new StringBuilder(webVtt ? "WEBVTT\n\n" : "");
        foreach (var s in segments)
        {
            var text = s.Text.Replace("-->", "→", StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(s.Speaker))
            {
                var speaker = speakerNames is not null && speakerNames.TryGetValue(s.Speaker, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name : s.Speaker;
                text = $"[{speaker}] {text}";
            }
            b.Append(s.Id + 1).Append('\n').Append(Stamp(s.Start, webVtt)).Append(" --> ")
                .Append(Stamp(s.End, webVtt)).Append('\n').Append(text).Append("\n\n");
        }
        return b.ToString();
    }
    private static string Stamp(double seconds, bool vtt)
    {
        var t = TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));
        return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{(vtt ? '.' : ',')}{t.Milliseconds:000}");
    }
}
