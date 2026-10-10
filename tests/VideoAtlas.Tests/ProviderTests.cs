using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class ProviderTests
{
    [Fact] public void LocalWhisperSupportsOnDeviceSpeakerDiarization()
    {
        var adapter = new LocalWhisperSpeechAdapter(Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalWhisperSpeechAdapter>.Instance);
        Assert.True(adapter.SupportsSpeakerDiarization);
    }
    [Fact] public async Task EnvironmentBindingsAreReferencedAtRuntimeAndNeverPersistedAsSecrets()
    {
        await using var app = new EnvironmentCredentialsAppFactory(); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IProviderAdministration>();
        var state = await admin.ReadAsync(default);
        Assert.Equal("speech-elevenlabs", state.Selection.Speech);
        Assert.Equal("chat-openrouter", state.Selection.Chat);
        Assert.Equal("embedding-openrouter", state.Selection.Embedding);
        Assert.True(state.Profiles.Single(p => p.Id == "chat-openrouter").UsesEnvironmentSecret);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.All(db.ProviderProfiles.ToArray(), p => Assert.Null(p.ProtectedSecret));
        var route = await scope.ServiceProvider.GetRequiredService<IAiProviderFactory>().ChatAsync(default);
        Assert.Equal("synthetic-environment-router", route!.Connection.Secret);
        Assert.DoesNotContain("synthetic-environment", JsonSerializer.Serialize(state));
    }
    [Fact] public async Task OldEmbeddingModelIsNotComparedWithNewProviderVectors()
    {
        var handler = new CaptureHandler(new { });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var video = new VideoAtlas.Domain.Video(Guid.NewGuid(), "owner", "old vectors", 1, TimeProvider.System.GetUtcNow());
        video.Complete(Enumerable.Range(0, 500).Select(i => new VideoAtlas.Domain.TranscriptSegment(i, i * 2, i * 2 + 2, new string('a',120))), [], "ai");
        video.SetIndex([new([2], [1, 0])], "previous-model");
        var index = services.GetRequiredService<TranscriptIndex>();
        Assert.Equal("stale", await index.StatusAsync(video, default));
        Assert.NotEmpty(await index.EvidenceAsync(video, new("Bütçe?"), default));
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task N8nEmbeddingPreservesOrderAndDimensions()
    {
        var vector = new float[512]; vector[7] = 1;
        var handler = new CaptureHandler(new { vectors = new[] { vector } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var result = await new N8nAdapter(services.GetRequiredService<ProviderTransport>()).EmbedAsync(Connection("n8n", host:"flow.example.invalid"), ["metin"], default);
        Assert.Single(result); Assert.Equal(1, result[0][7]); Assert.Contains("embedding", handler.Body);
    }
    [Fact] public async Task ProviderRedirectIsRejectedInsteadOfReturningAReply()
    {
        var handler = new CaptureHandler(new { }, HttpStatusCode.Redirect);
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var error = await Assert.ThrowsAsync<AiException>(() => new OpenRouterAdapter(services.GetRequiredService<ProviderTransport>()).CompleteJsonAsync(Connection("openrouter"), "json", "data", default));
        Assert.Equal("ai_unavailable", error.Code); Assert.Equal(1, handler.Calls);
    }
    private static ProviderConnection Connection(string kind, string model = "test-model", string? host = null, int dimensions = 512) =>
        new("test", kind, "https://" + (host ?? kind switch { "elevenlabs" => "api.elevenlabs.io", "openrouter" => "openrouter.ai", _ => "api.openai.com" }) + "/v1/", model, dimensions, "synthetic-secret");

    [Fact] public async Task OpenRouterUsesNamespacedModelAndDoesNotExposeSecretInConnectionString()
    {
        var handler = new CaptureHandler(new { choices = new[] { new { message = new { content = "{\"answer\":\"Merhaba\",\"segmentIds\":[0]}" } } } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var connection = Connection("openrouter", "openai/gpt-4.1-mini");
        var answer = await new OpenRouterAdapter(services.GetRequiredService<ProviderTransport>()).CompleteJsonAsync(connection, "json evidence only", "data", default);
        Assert.Contains("Merhaba", answer); Assert.Contains("openai/gpt-4.1-mini", handler.Body);
        Assert.Equal("openrouter.ai", handler.Host); Assert.Equal("Bearer synthetic-secret", handler.Authorization);
        Assert.DoesNotContain("synthetic-secret", connection.ToString());
    }
    [Fact] public async Task ElevenLabsUsesWordTimesAndChunkOffsets()
    {
        var handler = new CaptureHandler(new { words = new[]
        {
            new { type = "word", text = "Bütçe", start = 1d, end = 1.6 },
            new { type = "spacing", text = " ", start = 1.6, end = 1.6 },
            new { type = "word", text = "belirlendi.", start = 1.7, end = 3d },
            new { type = "audio_event", text = "(music)", start = 4d, end = 5d }
        } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var path = Path.GetTempFileName();
        try
        {
            var segments = await new ElevenLabsSpeechAdapter(services.GetRequiredService<ProviderTransport>()).TranscribeAsync(Connection("elevenlabs", "scribe_v1"), path, 480, default);
            Assert.Single(segments); Assert.Equal("Bütçe belirlendi.", segments[0].Text);
            Assert.Equal(481, segments[0].Start); Assert.Equal(483, segments[0].End);
            Assert.Equal("synthetic-secret", handler.Headers["xi-api-key"]); Assert.Null(handler.Authorization);
            Assert.Contains("scribe_v1", handler.Body); Assert.Contains("timestamps_granularity", handler.Body);
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task ElevenLabsRejectsInvalidTiming()
    {
        var handler = new CaptureHandler(new { words = new[] { new { type = "word", text = "Yanlış", start = -1, end = 3 } } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var path = Path.GetTempFileName();
        try { await Assert.ThrowsAsync<AiException>(() => new ElevenLabsSpeechAdapter(services.GetRequiredService<ProviderTransport>()).TranscribeAsync(Connection("elevenlabs"), path, 0, default)); }
        finally { File.Delete(path); }
    }
    [Fact] public async Task ElevenLabsRequestsDiarizationAndSeparatesSpeakers()
    {
        var handler = new CaptureHandler(new { words = new[]
        {
            new { type = "word", text = "Merhaba.", start = 0d, end = 1d, speaker_id = "speaker_0" },
            new { type = "word", text = "Hoş", start = 1.1d, end = 1.5d, speaker_id = "speaker_1" },
            new { type = "word", text = "bulduk.", start = 1.6d, end = 2.2d, speaker_id = "speaker_1" }
        } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var path = Path.GetTempFileName();
        try
        {
            var context = new VideoContext([], "", "tr", true, 2);
            var segments = await new ElevenLabsSpeechAdapter(services.GetRequiredService<ProviderTransport>())
                .TranscribeAsync(Connection("elevenlabs", "scribe_v2"), path, 0, context, default);
            Assert.Equal(2, segments.Count); Assert.Equal("speaker_0", segments[0].Speaker);
            Assert.Equal("speaker_1", segments[1].Speaker); Assert.Equal("Hoş bulduk.", segments[1].Text);
            Assert.Contains("name=diarize", handler.Body); Assert.Contains("true", handler.Body);
            Assert.Contains("name=num_speakers", handler.Body); Assert.Contains("2", handler.Body);
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task AzureSpeechConvertsMillisecondsAndUsesSubscriptionKey()
    {
        var handler = new CaptureHandler(new { phrases = new[] { new { offsetMilliseconds = 1500, durationMilliseconds = 2000, text = "Azure konuşması." } } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var path = Path.GetTempFileName();
        try
        {
            var result = await new AzureSpeechAdapter(services.GetRequiredService<ProviderTransport>()).TranscribeAsync(Connection("azure-speech", "tr-TR", "speech.example.invalid"), path, 480, default);
            Assert.Equal(481.5, result[0].Start); Assert.Equal(483.5, result[0].End);
            Assert.Equal("synthetic-secret", handler.Headers["Ocp-Apim-Subscription-Key"]);
            Assert.Contains("api-version=2024-11-15", handler.Path); Assert.Contains("tr-TR", handler.Body);
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task FoundryUsesDeploymentNameAndApiKeyHeader()
    {
        var handler = new CaptureHandler(new { choices = new[] { new { message = new { content = "{}" } } } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        await new AzureFoundryAdapter(services.GetRequiredService<ProviderTransport>()).CompleteJsonAsync(Connection("azure-foundry", "my-deployment", "foundry.example.invalid"), "json", "test", default);
        Assert.Contains("my-deployment", handler.Body); Assert.Equal("synthetic-secret", handler.Headers["api-key"]); Assert.Null(handler.Authorization);
    }
    [Fact] public async Task N8nUsesExactWebhookAndNormalizedContract()
    {
        var handler = new CaptureHandler(new { content = "{\"answer\":\"Akış yanıtı\",\"segmentIds\":[1]}" });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var route = new ProviderConnection("workflow", "n8n", "https://tenant.app.n8n.cloud/webhook/chat", "v1", 512, "synthetic-secret");
        var result = await new N8nAdapter(services.GetRequiredService<ProviderTransport>()).CompleteJsonAsync(route, "system", "data", default);
        Assert.Contains("Akış", result); Assert.Equal("/webhook/chat", handler.Path); Assert.Contains("contractVersion", handler.Body);
    }
    [Fact] public async Task EmbeddingAdapterRejectsDuplicateIndicesOrWrongDimensions()
    {
        var handler = new CaptureHandler(new { data = new[] { new { index = 0, embedding = new[] { 1f, 0f } }, new { index = 0, embedding = new[] { 1f, 0f } } } });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        await Assert.ThrowsAsync<AiException>(() => new OpenRouterAdapter(services.GetRequiredService<ProviderTransport>()).EmbedAsync(Connection("openrouter"), ["a", "b"], default));
    }
    [Theory]
    [InlineData("http://flow.example.invalid/webhook", "n8n")]
    [InlineData("https://127.0.0.1/", "n8n")]
    [InlineData("https://openrouter.ai.attacker.invalid/", "openrouter")]
    [InlineData("https://user:password@openrouter.ai/v1/", "openrouter")]
    [InlineData("https://openrouter.ai:444/v1/", "openrouter")]
    [InlineData("https://openrouter.ai/v1/?api_key=secret", "openrouter")]
    [InlineData("https://flow.example.invalid/v1/", "elevenlabs")]
    public async Task EndpointPolicyRejectsUntrustedAddressesBeforeSending(string endpoint, string kind)
    {
        var handler = new CaptureHandler(new { });
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        await Assert.ThrowsAsync<AppException>(() => services.GetRequiredService<ProviderTransport>().PostAsync(new("bad", kind, endpoint, "model", 512, "secret"), "", new StringContent("{}"), default));
        Assert.Null(handler.Path);
    }
    [Fact] public async Task PaidCallsAreNotRetriedAndErrorsDoNotEchoProviderBody()
    {
        var handler = new CaptureHandler(new { error = "synthetic-secret and private transcript" }, HttpStatusCode.TooManyRequests);
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var error = await Assert.ThrowsAsync<AiException>(() => new OpenRouterAdapter(services.GetRequiredService<ProviderTransport>()).CompleteJsonAsync(Connection("openrouter"), "json", "data", default));
        Assert.Equal("ai_quota", error.Code); Assert.Equal(1, handler.Calls); Assert.DoesNotContain("private transcript", error.Message);
    }
    [Fact] public async Task SettingsEncryptSecretsValidateSelectionAndFreezeRoutingPerScope()
    {
        await using var app = new VideoAppFactory(); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IProviderAdministration>();
        var state = await admin.ReadAsync(default);
        var p = state.Profiles.Single(p => p.Id == "chat-openrouter");
        await admin.SaveAsync(new(p.Id, p.Endpoint, p.Model, p.Dimensions, "synthetic-secret-value", false, p.Version), "test-admin", default);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.ProviderProfiles.FindAsync(p.Id);
        Assert.NotNull(entity!.ProtectedSecret); Assert.DoesNotContain("synthetic-secret-value", entity.ProtectedSecret);
        Assert.DoesNotContain("synthetic-secret-value", JsonSerializer.Serialize(await admin.ReadAsync(default)));
        var factory = scope.ServiceProvider.GetRequiredService<IAiProviderFactory>();
        Assert.Null(await factory.ChatAsync(default)); // pin disabled snapshot
        state = await admin.ReadAsync(default);
        await admin.SelectAsync(new(null, p.Id, null, state.Selection.Version), "test-admin", default);
        Assert.Null(await factory.ChatAsync(default)); // existing job does not switch halfway
        using var fresh = app.Services.CreateScope();
        var route = await fresh.ServiceProvider.GetRequiredService<IAiProviderFactory>().ChatAsync(default);
        Assert.IsType<OpenRouterAdapter>(route!.Adapter);
        Assert.Equal("synthetic-secret-value", route.Connection.Secret);
        Assert.Throws<CryptographicException>(() => fresh.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("wrong-purpose").Unprotect(entity.ProtectedSecret!));
        var stale = await Assert.ThrowsAsync<AppException>(() => admin.SelectAsync(new(null, null, null, state.Selection.Version), "test", default));
        Assert.Equal(409, stale.Status);
    }
    [Fact] public async Task SelectionRejectsMissingCredentialsAndWrongCapability()
    {
        await using var app = new VideoAppFactory(); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IProviderAdministration>();
        var state = await admin.ReadAsync(default);
        var missing = await Assert.ThrowsAsync<AppException>(() => admin.SelectAsync(new("speech-elevenlabs", null, null, state.Selection.Version), "test", default));
        Assert.Equal("provider_secret_missing", missing.Code);
        var wrong = await Assert.ThrowsAsync<AppException>(() => admin.SelectAsync(new("chat-openrouter", null, null, state.Selection.Version), "test", default));
        Assert.Equal("provider_capability", wrong.Code);
    }
    [Fact] public void IndexFingerprintChangesWithProviderModelEndpointAndDimensionsButNotKey()
    {
        var first = Connection("openrouter", "openai/text-embedding-3-small");
        var rotated = new ProviderConnection("other-profile", first.Kind, first.Endpoint, first.Model, first.Dimensions, "rotated-secret");
        Assert.Equal(TranscriptIndex.Fingerprint(first), TranscriptIndex.Fingerprint(rotated));
        Assert.NotEqual(TranscriptIndex.Fingerprint(first), TranscriptIndex.Fingerprint(Connection("openai", first.Model)));
        Assert.NotEqual(TranscriptIndex.Fingerprint(first), TranscriptIndex.Fingerprint(Connection("openrouter", "other-model")));
        Assert.NotEqual(TranscriptIndex.Fingerprint(first), TranscriptIndex.Fingerprint(Connection("openrouter", first.Model, dimensions: 1024)));
    }
}
internal sealed class EnvironmentCredentialsAppFactory : VideoAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.PostConfigure<ProviderCredentialOptions>(o =>
        { o.OpenRouter = "synthetic-environment-router"; o.ElevenLabs = "synthetic-environment-speech"; }));
    }
}
internal sealed class CaptureHandler(object payload, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public string? Path { get; private set; }
    public string? Host { get; private set; }
    public string? Authorization { get; private set; }
    public string Body { get; private set; } = "";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Calls { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++; Path = request.RequestUri!.PathAndQuery; Host = request.RequestUri.IdnHost; Authorization = request.Headers.Authorization?.ToString();
        foreach (var header in request.Headers) Headers[header.Key] = string.Join(",", header.Value);
        Body = await request.Content!.ReadAsStringAsync(ct);
        return new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
    }
}
