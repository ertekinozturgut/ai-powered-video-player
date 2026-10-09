using System.Security.Cryptography;
using System.Text;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed record IndexBuild(IReadOnlyList<TranscriptVector> Vectors, string Fingerprint);
public sealed class TranscriptIndex(IAiProviderFactory providers)
{
    public static string Fingerprint(ProviderConnection connection) =>
        "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            connection.Kind, connection.Endpoint, connection.Model, connection.Dimensions))));
    public async Task<string> StatusAsync(Video video, CancellationToken ct)
    {
        var route = await providers.EmbeddingAsync(ct);
        if (route is null) return "disabled";
        if (video.Vectors.Count == 0) return "missing";
        return video.EmbeddingModel == Fingerprint(route.Connection) ? "ready" : "stale";
    }
    public async Task<IndexBuild> BuildAsync(IReadOnlyList<TranscriptSegment> segments, CancellationToken ct)
    {
        var route = await providers.EmbeddingAsync(ct);
        if (route is null) return new([], "");
        var groups = Groups(segments).ToArray();
        var result = new List<TranscriptVector>();
        foreach (var batch in groups.Chunk(48))
        {
            var embeddings = await route.Adapter.EmbedAsync(route.Connection, batch.Select(g => string.Join(' ', g.Select(s => s.Text))).ToArray(), ct);
            result.AddRange(batch.Select((g, i) => new TranscriptVector(g.Select(s => s.Id).ToArray(), embeddings[i])));
        }
        return new(result, Fingerprint(route.Connection));
    }
    public async Task<IReadOnlyList<TranscriptSegment>> EvidenceAsync(Video video, ChatRequest request, CancellationToken ct)
    {
        var route = await providers.EmbeddingAsync(ct);
        if (route is null || video.Segments.Sum(s => s.Text.Length) <= 48_000 || video.Vectors.Count == 0 || video.EmbeddingModel != Fingerprint(route.Connection))
            return TranscriptSearch.Context(video.Segments, request.Question);
        var question = string.Join(' ', (request.History ?? []).Where(h => h.Role == "user").TakeLast(2).Select(h => h.Content)) + " " + request.Question;
        var query = (await route.Adapter.EmbedAsync(route.Connection, [question], ct))[0];
        var ids = video.Vectors.OrderByDescending(v => Cosine(query, v.Values)).Take(12).SelectMany(v => v.SegmentIds).ToHashSet();
        var selected = video.Segments.Where(s => ids.Contains(s.Id) || ids.Contains(s.Id - 1) || ids.Contains(s.Id + 1)).ToList();
        // Chapter opening segments give broad questions chronological coverage, in addition to semantic matches.
        selected.AddRange(video.Chapters.Select(c => video.Segments.FirstOrDefault(s => s.Start >= c.Start)).OfType<TranscriptSegment>().Take(40));
        var length = 0;
        return selected.DistinctBy(s => s.Id).OrderBy(s => s.Start).TakeWhile(s => (length += s.Text.Length) <= 48_000).ToArray();
    }
    public static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count || left.Count == 0) return 0;
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < left.Count; i++) { dot += left[i] * right[i]; a += left[i] * left[i]; b += right[i] * right[i]; }
        return a == 0 || b == 0 ? 0 : dot / Math.Sqrt(a * b);
    }
    private static IEnumerable<List<TranscriptSegment>> Groups(IReadOnlyList<TranscriptSegment> segments)
    {
        var group = new List<TranscriptSegment>(); var length = 0;
        foreach (var s in segments)
        {
            if (length + s.Text.Length > 1600 && group.Count > 0) { yield return group; group = []; length = 0; }
            group.Add(s); length += s.Text.Length;
        }
        if (group.Count > 0) yield return group;
    }
}
