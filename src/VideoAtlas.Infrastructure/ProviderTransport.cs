using System.Net.Http.Headers;
using System.Text.Json;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class ProviderTransport(IHttpClientFactory clients, ProviderEndpointPolicy endpoints,
    AiUsageRecorder? usage = null, AiUsageContext? context = null)
{
    public async Task<JsonDocument> PostAsync(ProviderConnection connection, string path, HttpContent content, CancellationToken ct)
    {
        endpoints.Validate(connection.Endpoint, connection.Kind);
        using var request = new HttpRequestMessage(HttpMethod.Post, path.Length == 0 ? new Uri(connection.Endpoint) : new Uri(new Uri(connection.Endpoint), path)) { Content = content };
        if (connection.Kind == "elevenlabs") request.Headers.Add("xi-api-key", connection.Secret);
        else if (connection.Kind == "azure-speech") request.Headers.Add("Ocp-Apim-Subscription-Key", connection.Secret);
        else if (connection.Kind == "azure-foundry") request.Headers.Add("api-key", connection.Secret);
        else if (connection.Kind != "ollama") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Secret);
        request.Headers.Add("X-Correlation-Id", System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"));
        using var client = clients.CreateClient("AiProviders");
        // Buffering enforces MaxResponseContentBufferSize. Never retry paid POSTs or follow redirects.
        var entry = usage is not null && context is not null ? await usage.ReserveAsync(context, connection, path, content, ct) : null;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode) throw new AiException((int)response.StatusCode switch
            {
                401 or 403 => "ai_credentials", 402 or 429 => "ai_quota", 400 or 404 or 422 => "ai_configuration", _ => "ai_unavailable"
            });
            JsonDocument json;
            try { json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct), new() { MaxDepth = 32 }); }
            catch (JsonException) { throw new AiException("ai_invalid_response"); }
            if (usage is not null) await usage.FinishAsync(entry, json.RootElement, "success", null);
            return json;
        }
        catch (Exception ex) when (ex is AiException or HttpRequestException or OperationCanceledException)
        {
            if (usage is not null) await usage.FinishAsync(entry, null, "uncertain", ex is AiException ai ? ai.Code : "connection_or_cancel");
            throw;
        }
    }
}
internal static class ProviderResponse
{
    public static T Read<T>(Func<T> parse)
    {
        try { return parse(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException)
        { throw new AiException("ai_invalid_response"); }
    }
    public static IReadOnlyList<TranscriptSegment> Segments(JsonElement array, double offset)
    {
        var segments = Read(() => array.EnumerateArray().Select((s, i) => new TranscriptSegment(i,
            offset + s.GetProperty("start").GetDouble(), offset + s.GetProperty("end").GetDouble(),
            s.GetProperty("text").GetString()?.Trim() ?? "")).ToArray());
        return ValidateSegments(segments, offset);
    }
    public static IReadOnlyList<TranscriptSegment> ValidateSegments(IReadOnlyList<TranscriptSegment> segments, double offset)
    {
        double previous = offset;
        foreach (var s in segments)
        {
            if (!double.IsFinite(s.Start) || !double.IsFinite(s.End) || s.Start < previous || s.End <= s.Start ||
                s.End > offset + 481 || s.Text.Length > 16000)
                throw new AiException("ai_invalid_response");
            previous = s.Start;
        }
        return segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToArray();
    }
    public static float[][] Vectors(JsonElement array, int count, int dimensions, bool openAi)
    {
        var vectors = Read(() =>
        {
            var items = array.EnumerateArray().ToArray();
            if (openAi)
            {
                items = items.OrderBy(v => v.GetProperty("index").GetInt32()).ToArray();
                if (!items.Select(v => v.GetProperty("index").GetInt32()).SequenceEqual(Enumerable.Range(0, count)))
                    throw new AiException("ai_invalid_response");
            }
            return items.Select(v => (openAi ? v.GetProperty("embedding") : v).EnumerateArray().Select(n => n.GetSingle()).ToArray()).ToArray();
        });
        if (vectors.Length != count || vectors.Any(v => v.Length != dimensions || v.Any(n => !float.IsFinite(n)) || !v.Any(n => n != 0)))
            throw new AiException("ai_invalid_response");
        return vectors;
    }
}
public sealed class AiException(string code) : Exception(code) { public string Code { get; } = code; }
