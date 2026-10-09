using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class AiPipelineTests
{
    [Fact] public async Task ElevenLabsToOpenRouterPipelineUsesSelectedFactoriesEndToEnd()
    {
        var handler = new ControlledProvider();
        await using var app = new RoutedVideoAppFactory(handler);
        using var client = app.CreateClient();
        var fixture = await app.FixtureAsync();
        Guid id;
        using (var scope = app.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IProviderAdministration>();
            foreach (var profileId in new[] { "speech-elevenlabs", "chat-openrouter", "embedding-openrouter" })
            {
                var p = (await admin.ReadAsync(default)).Profiles.Single(p => p.Id == profileId);
                await admin.SaveAsync(new(p.Id, p.Endpoint, p.Model, p.Dimensions, "synthetic-route-secret", false, p.Version), "test-admin", default);
            }
            var state = await admin.ReadAsync(default);
            await admin.SelectAsync(new("speech-elevenlabs", "chat-openrouter", "embedding-openrouter", state.Selection.Version), "test-admin", default);
            await using var stream = File.OpenRead(fixture);
            id = (await scope.ServiceProvider.GetRequiredService<VideoLibrary>().UploadAsync(new("owner", "Two-provider fixture", stream.Length, true, stream), default)).Id;
        }
        Video? video = null;
        for (var i = 0; i < 100; i++)
        {
            using var scope = app.Services.CreateScope();
            video = await scope.ServiceProvider.GetRequiredService<IVideoStore>().FindAsync(id, default);
            if (!video!.IsBusy) break;
            await Task.Delay(200);
        }
        Assert.NotNull(video); Assert.Equal(VideoStatus.Ready, video.Status);
        Assert.Equal("ai", video.AnalysisMode); Assert.Equal(3, video.Segments.Count); Assert.NotEmpty(video.Vectors);
        using var chatScope = app.Services.CreateScope();
        var answer = await chatScope.ServiceProvider.GetRequiredService<VideoChat>().AskAsync(video, new("Bütçe?"), default);
        Assert.Equal("ai", answer.Mode); Assert.Equal(3, Assert.Single(answer.Citations).Start);
        Assert.Contains("api.elevenlabs.io", handler.Hosts); Assert.Contains("openrouter.ai", handler.Hosts);
        Assert.DoesNotContain("api.openai.com", handler.Hosts);
        Assert.Equal("ready", await chatScope.ServiceProvider.GetRequiredService<TranscriptIndex>().StatusAsync(video, default));
    }
    [Fact] public async Task RealMediaWorkerAutomaticallyTranscribesCategorizesAndIndexesWithControlledProvider()
    {
        await using var app = new AiVideoAppFactory();
        using var client = app.CreateClient();
        var fixture = await app.FixtureAsync();
        Guid id;
        using (var scope = app.Services.CreateScope())
        {
            await using var stream = File.OpenRead(fixture);
            var service = scope.ServiceProvider.GetRequiredService<VideoLibrary>();
            id = (await service.UploadAsync(new("synthetic-owner", "AI pipeline fixture", stream.Length, true, stream), CancellationToken.None)).Id;
        }
        Video? video = null;
        for (var i = 0; i < 100; i++)
        {
            using var scope = app.Services.CreateScope();
            video = await scope.ServiceProvider.GetRequiredService<IVideoStore>().FindAsync(id, CancellationToken.None);
            if (!video!.IsBusy) break;
            await Task.Delay(200);
        }
        Assert.NotNull(video); Assert.Equal(VideoStatus.Ready, video.Status);
        Assert.Equal("ai", video.AnalysisMode); Assert.Equal(3, video.Segments.Count); Assert.Equal(2, video.Chapters.Count);
        Assert.NotEmpty(video.Vectors); Assert.Equal(512, video.Vectors[0].Values.Length);
        Assert.Equal(10, video.Duration, 1);
        using var chatScope = app.Services.CreateScope();
        var answer = await chatScope.ServiceProvider.GetRequiredService<VideoChat>().AskAsync(video, new("Bütçe?"), CancellationToken.None);
        Assert.Equal("ai", answer.Mode); Assert.Equal(3, Assert.Single(answer.Citations).Start);
    }

    [Fact] public async Task SemanticRetrievalFindsRelevantLongVideoSegmentWithoutKeywordOverlap()
    {
        var services = ProviderTestServices.Create(new ControlledProvider());
        await using var provider = services.BuildServiceProvider();
        var video = new Video(Guid.NewGuid(), "owner", "Long fixture", 1, TimeProvider.System.GetUtcNow());
        var segments = Enumerable.Range(0, 500).Select(i => new TranscriptSegment(i, i * 3, i * 3 + 3, new string('a', 120))).ToArray();
        segments[217] = new(217, 651, 654, "Maliyetler düşürülecek.");
        video.Complete(segments, [], "ai");
        var relevant = new float[512]; relevant[0] = 1;
        var irrelevant = new float[512]; irrelevant[1] = 1;
        video.SetIndex(Enumerable.Range(0, 20).Select(i => new TranscriptVector([i], irrelevant))
            .Append(new TranscriptVector([217], relevant)), TranscriptIndex.Fingerprint(TestProviderFactory.EmbeddingConnection));
        var result = await provider.GetRequiredService<TranscriptIndex>().EvidenceAsync(video, new("Tasarruf planımız nedir?"), CancellationToken.None);
        Assert.Contains(result, s => s.Id == 217); Assert.DoesNotContain(result, s => s.Id == 499);
        Assert.Equal(1, TranscriptIndex.Cosine(relevant, relevant), 6);
        Assert.Equal(0, TranscriptIndex.Cosine(relevant, irrelevant), 6);
    }
}

internal sealed class AiVideoAppFactory : VideoAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<AiOptions>(o => o.ApiKey = "synthetic-test-key");
            services.AddHttpClient("AiProviders").ConfigurePrimaryHttpMessageHandler(() => new ControlledProvider());
        });
    }
}
internal sealed class ControlledProvider : HttpMessageHandler
{
    public System.Collections.Concurrent.ConcurrentBag<string> Hosts { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Hosts.Add(request.RequestUri!.IdnHost);
        object response;
        if (request.RequestUri!.AbsolutePath.EndsWith("/speech-to-text", StringComparison.Ordinal))
            response = new { words = Subtitles.Parse(CoreTests.Transcript, 10).Select(s => new { type = "word", start = s.Start, end = s.End, text = s.Text }) };
        else if (request.RequestUri!.AbsolutePath.EndsWith("/transcriptions", StringComparison.Ordinal))
            response = new { segments = Subtitles.Parse(CoreTests.Transcript, 10).Select(s => new { start = s.Start, end = s.End, text = s.Text }) };
        else
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (request.RequestUri.AbsolutePath.EndsWith("/embeddings", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(body);
                var count = json.RootElement.GetProperty("input").GetArrayLength();
                var vector = new float[512]; vector[0] = 1;
                response = new { data = Enumerable.Range(0, count).Select(i => new { index = i, embedding = vector }) };
            }
            else
            {
                var inner = body.Contains("firstSegmentId", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new { chapters = new[] { new { title = "Ürün planı", summary = "Lansman Ekim ayında", category = "Strateji", firstSegmentId = 0 }, new { title = "Bütçe", summary = "Yüz bin lira", category = "Finans", firstSegmentId = 1 } } })
                    : JsonSerializer.Serialize(new { answer = "Bütçe yüz bin lira.", segmentIds = new[] { 1 } });
                response = new { choices = new[] { new { message = new { content = inner } } } };
            }
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
    }
}
internal sealed class RoutedVideoAppFactory(ControlledProvider handler) : VideoAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddHttpClient("AiProviders").ConfigurePrimaryHttpMessageHandler(() => handler));
    }
}
