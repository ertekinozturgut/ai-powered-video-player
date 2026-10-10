using System.Net.Http.Json;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

// Synchronous webhook contract, version 1. Separate saved endpoint per capability.
public sealed class N8nAdapter(ProviderTransport transport) : ISpeechToTextAdapter, ILlmAdapter, IEmbeddingAdapter
{
    public string Kind => "n8n";
    public bool SupportsSpeakerDiarization => true;
    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct) => TranscribeAsync(connection, audioPath, offset, VideoContext.Empty, ct);
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "file");
        body.Add(new StringContent("speech"), "operation");
        body.Add(new StringContent("1"), "contractVersion");
        body.Add(new StringContent(connection.Model), "model");
        body.Add(new StringContent(context.DiarizationEnabled ? "true" : "false"), "diarizationEnabled");
        if (context.DiarizationEnabled && context.SpeakerCount is { } speakerCount)
            body.Add(new StringContent(speakerCount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "speakerCount");
        if (context.Terms.Count > 0) body.Add(new StringContent(System.Text.Json.JsonSerializer.Serialize(context.Terms, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))), "terms");
        using var json = await transport.PostAsync(connection, "", body, ct);
        return ProviderResponse.Segments(ProviderResponse.Read(() => json.RootElement.GetProperty("segments")), offset,
            context.DiarizationEnabled ? 7200 : 480);
    }
    public async Task<string> CompleteJsonAsync(ProviderConnection connection, string system, string data, CancellationToken ct)
    {
        using var body = JsonContent.Create(new { operation = "chat", contractVersion = 1, model = connection.Model,
            system, data, responseFormat = "json", maxOutputTokens = 2500 });
        using var json = await transport.PostAsync(connection, "", body, ct);
        return ProviderResponse.Read(() => json.RootElement.GetProperty("content").GetString()) ?? throw new AiException("ai_invalid_response");
    }
    public async Task<float[][]> EmbedAsync(ProviderConnection connection, string[] texts, CancellationToken ct)
    {
        using var body = JsonContent.Create(new { operation = "embedding", contractVersion = 1, model = connection.Model, input = texts, dimensions = connection.Dimensions });
        using var json = await transport.PostAsync(connection, "", body, ct);
        return ProviderResponse.Vectors(ProviderResponse.Read(() => json.RootElement.GetProperty("vectors")), texts.Length, connection.Dimensions, false);
    }
}
