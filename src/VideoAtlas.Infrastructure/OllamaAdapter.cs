using System.Net.Http.Json;
using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

public sealed class OllamaAdapter(ProviderTransport transport) : ILlmAdapter, IEmbeddingAdapter
{
    public string Kind => "ollama";

    public async Task<string> CompleteJsonAsync(ProviderConnection connection, string system, string data, CancellationToken ct)
    {
        using var body = JsonContent.Create(new
        {
            model = connection.Model, stream = false, format = "json",
            messages = new[] { new { role = "system", content = system }, new { role = "user", content = data } }
        });
        using var json = await transport.PostAsync(connection, "api/chat", body, ct);
        var content = ProviderResponse.Read(() => json.RootElement.GetProperty("message").GetProperty("content").GetString());
        if (string.IsNullOrWhiteSpace(content)) throw new AiException("ai_invalid_response");
        return content;
    }

    public async Task<float[][]> EmbedAsync(ProviderConnection connection, string[] texts, CancellationToken ct)
    {
        using var body = JsonContent.Create(new { model = connection.Model, input = texts });
        using var json = await transport.PostAsync(connection, "api/embed", body, ct);
        return ProviderResponse.Vectors(ProviderResponse.Read(() => json.RootElement.GetProperty("embeddings")), texts.Length, connection.Dimensions, false);
    }
}
