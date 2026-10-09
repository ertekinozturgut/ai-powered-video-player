using System.Text;
using System.Text.Json;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class ElevenLabsSpeechAdapter(ProviderTransport transport) : ISpeechToTextAdapter
{
    public string Kind => "elevenlabs";
    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct) => TranscribeAsync(connection, audioPath, offset, VideoContext.Empty, ct);
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "file");
        body.Add(new StringContent(connection.Model), "model_id");
        body.Add(new StringContent("word"), "timestamps_granularity");
        body.Add(new StringContent("false"), "diarize");
        body.Add(new StringContent("false"), "tag_audio_events");
        // Scribe v1 does not support keyterm prompting. Never send unsupported fields to it.
        if (connection.Model == "scribe_v2") foreach (var term in context.Terms.Take(50)) body.Add(new StringContent(term.Term), "keyterms");
        using var json = await transport.PostAsync(connection, "speech-to-text", body, ct);
        var words = ProviderResponse.Read(() => json.RootElement.GetProperty("words").EnumerateArray()
            .Where(w => w.GetProperty("type").GetString() == "word")
            .Select(w => new Word(w.GetProperty("start").GetDouble(), w.GetProperty("end").GetDouble(),
                w.GetProperty("text").GetString() ?? "")).ToArray());
        var result = new List<TranscriptSegment>();
        var text = new StringBuilder();
        double start = 0, end = 0, previous = 0;
        void Flush()
        {
            if (text.Length == 0) return;
            result.Add(new(result.Count, offset + start, offset + Math.Max(end, start + .01), text.ToString().Trim()));
            text.Clear();
        }
        foreach (var word in words)
        {
            if (!double.IsFinite(word.Start) || !double.IsFinite(word.End) || word.Start < previous || word.End < word.Start || word.End > 480.5)
                throw new AiException("ai_invalid_response");
            previous = word.Start;
            if (string.IsNullOrWhiteSpace(word.Text)) continue;
            if (text.Length > 0 && (word.Start - start >= 7 || text.Length + word.Text.Length > 220)) Flush();
            if (text.Length == 0) start = word.Start;
            else text.Append(' ');
            text.Append(word.Text.Trim()); end = word.End;
            if (word.Text.EndsWith('.') || word.Text.EndsWith('?') || word.Text.EndsWith('!')) Flush();
        }
        Flush();
        return ProviderResponse.ValidateSegments(result, offset);
    }
    private sealed record Word(double Start, double End, string Text);
}
public sealed class OpenAiSpeechAdapter(ProviderTransport transport) : ISpeechToTextAdapter
{
    public string Kind => "openai";
    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct) => TranscribeAsync(connection, audioPath, offset, VideoContext.Empty, ct);
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "file");
        body.Add(new StringContent(connection.Model), "model");
        body.Add(new StringContent("verbose_json"), "response_format");
        body.Add(new StringContent("segment"), "timestamp_granularities[]");
        if (context.Terms.Count > 0)
        {
            var vocabulary = string.Join(", ", context.Terms.Select(t => t.Term));
            body.Add(new StringContent(vocabulary[..Math.Min(800, vocabulary.Length)]), "prompt");
        }
        using var json = await transport.PostAsync(connection, "audio/transcriptions", body, ct);
        return ProviderResponse.Segments(ProviderResponse.Read(() => json.RootElement.GetProperty("segments")), offset);
    }
}
public sealed class AzureSpeechAdapter(ProviderTransport transport) : ISpeechToTextAdapter
{
    public string Kind => "azure-speech";
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "audio");
        body.Add(new StringContent(JsonSerializer.Serialize(new { locales = new[] { connection.Model }, profanityFilterMode = "None" }), Encoding.UTF8, "application/json"), "definition");
        using var json = await transport.PostAsync(connection, "speechtotext/transcriptions:transcribe?api-version=2024-11-15", body, ct);
        var result = ProviderResponse.Read(() => json.RootElement.GetProperty("phrases").EnumerateArray().Select((p, i) =>
            new TranscriptSegment(i, offset + p.GetProperty("offsetMilliseconds").GetDouble() / 1000,
                offset + (p.GetProperty("offsetMilliseconds").GetDouble() + p.GetProperty("durationMilliseconds").GetDouble()) / 1000,
                p.GetProperty("text").GetString() ?? "")).OrderBy(s => s.Start).ToArray());
        return ProviderResponse.ValidateSegments(result, offset);
    }
}
internal static class SpeechMultipart
{
    public static MultipartFormDataContent Create(Stream stream, string name)
    {
        var body = new MultipartFormDataContent();
        var audio = new StreamContent(stream);
        audio.Headers.ContentType = new("audio/mpeg");
        body.Add(audio, name, "audio.mp3");
        return body;
    }
}
