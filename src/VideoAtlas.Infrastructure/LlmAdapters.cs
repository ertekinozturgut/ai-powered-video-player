using System.Net.Http.Json;
using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

// OpenRouter and Foundry expose the OpenAI v1 wire contract, not OpenAI credentials.
public abstract class OpenAiCompatibleAdapter(ProviderTransport transport) : ILlmAdapter, IEmbeddingAdapter
{
    public abstract string Kind { get; }
    public async Task<string> CompleteJsonAsync(ProviderConnection connection, string system, string data, CancellationToken ct)
    {
        using var body = JsonContent.Create(new
        {
            model = connection.Model, temperature = 0.1, max_tokens = 2500, response_format = new { type = "json_object" },
            messages = new[] { new { role = "system", content = system }, new { role = "user", content = data } }
        });
        using var json = await transport.PostAsync(connection, "chat/completions", body, ct);
        var result = ProviderResponse.Read(() => json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        if (string.IsNullOrWhiteSpace(result)) throw new AiException("ai_invalid_response");
        return result;
    }
    public async Task<float[][]> EmbedAsync(ProviderConnection connection, string[] texts, CancellationToken ct)
    {
        using var body = JsonContent.Create(new { model = connection.Model, input = texts, encoding_format = "float", dimensions = connection.Dimensions });
        using var json = await transport.PostAsync(connection, "embeddings", body, ct);
        return ProviderResponse.Vectors(ProviderResponse.Read(() => json.RootElement.GetProperty("data")), texts.Length, connection.Dimensions, true);
    }
}
public sealed class OpenRouterAdapter(ProviderTransport transport) : OpenAiCompatibleAdapter(transport) { public override string Kind => "openrouter"; }
public sealed class AzureFoundryAdapter(ProviderTransport transport) : OpenAiCompatibleAdapter(transport) { public override string Kind => "azure-foundry"; }
public sealed class OpenAiAdapter(ProviderTransport transport) : OpenAiCompatibleAdapter(transport) { public override string Kind => "openai"; }
