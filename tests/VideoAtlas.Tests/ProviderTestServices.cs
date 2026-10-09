using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Application;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Tests;

internal static class ProviderTestServices
{
    public static ServiceCollection Create(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddOptions<ProviderSecurityOptions>().Configure(o => o.AllowedHosts =
            ["api.openai.com", "openrouter.ai", "api.elevenlabs.io", "speech.example.invalid", "foundry.example.invalid", "flow.example.invalid"]);
        services.AddSingleton<ProviderEndpointPolicy>();
        services.AddHttpClient("AiProviders", c => c.MaxResponseContentBufferSize = 8 * 1024 * 1024).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddScoped<ProviderTransport>();
        services.AddScoped<IAiProviderFactory, TestProviderFactory>();
        services.AddScoped<VideoIntelligence>();
        services.AddScoped<TranscriptIndex>();
        return services;
    }
}
internal sealed class TestProviderFactory(ProviderTransport transport) : IAiProviderFactory
{
    public static readonly ProviderConnection EmbeddingConnection = new("embedding-openai", "openai", "https://api.openai.com/v1/", "text-embedding-3-small", 512, "synthetic-test-key");
    public Task<ProviderBinding<ISpeechToTextAdapter>?> SpeechAsync(CancellationToken ct) => Task.FromResult<ProviderBinding<ISpeechToTextAdapter>?>(
        new(new("speech-openai", "openai", "https://api.openai.com/v1/", "whisper-1", 512, "synthetic-test-key"), new OpenAiSpeechAdapter(transport)));
    public Task<ProviderBinding<ILlmAdapter>?> ChatAsync(CancellationToken ct) => Task.FromResult<ProviderBinding<ILlmAdapter>?>(
        new(new("chat-openai", "openai", "https://api.openai.com/v1/", "gpt-4o-mini", 512, "synthetic-test-key"), new OpenAiAdapter(transport)));
    public Task<ProviderBinding<IEmbeddingAdapter>?> EmbeddingAsync(CancellationToken ct) => Task.FromResult<ProviderBinding<IEmbeddingAdapter>?>(
        new(EmbeddingConnection, new OpenAiAdapter(transport)));
}
