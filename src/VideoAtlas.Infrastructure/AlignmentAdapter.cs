using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

public sealed class ElevenLabsAlignmentAdapter(ProviderTransport transport) : ITranscriptAlignmentAdapter
{
    public string Kind => "elevenlabs";
    public async Task<(double Start, double End)> AlignAsync(ProviderConnection connection, string audioPath, string text, double offset, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "file");
        body.Add(new StringContent(text), "text");
        using var json = await transport.PostAsync(connection, "forced-alignment", body, ct);
        return ProviderResponse.Read(() =>
        {
            var words = json.RootElement.GetProperty("words").EnumerateArray().ToArray();
            if (words.Length == 0) throw new AiException("ai_invalid_response");
            double previous = 0;
            foreach (var word in words)
            {
                var start = word.GetProperty("start").GetDouble(); var end = word.GetProperty("end").GetDouble();
                if (!double.IsFinite(start) || !double.IsFinite(end) || start < previous || end <= start) throw new AiException("ai_invalid_response");
                previous = start;
            }
            return (offset + words[0].GetProperty("start").GetDouble(), offset + words[^1].GetProperty("end").GetDouble());
        });
    }
}
