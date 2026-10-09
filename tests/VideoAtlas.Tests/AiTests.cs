using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class AiTests
{
    [Fact] public async Task ChatRejectsInventedCitationIds()
    {
        var video = ReadyVideo();
        var chat = new VideoChat(new FakeIntelligence(new("Unsupported claim", [999])), new QuotaStore());
        var answer = await chat.AskAsync(video, new("Bütçe?"), CancellationToken.None);
        Assert.Empty(answer.Citations);
        Assert.DoesNotContain("Unsupported claim", answer.Answer);
    }
    [Fact] public async Task ChatReturnsOnlyActualSegmentTimes()
    {
        var chat = new VideoChat(new FakeIntelligence(new("Yüz bin lira.", [1])), new QuotaStore());
        var answer = await chat.AskAsync(ReadyVideo(), new("Bütçe?"), CancellationToken.None);
        Assert.Equal(3, Assert.Single(answer.Citations).Start);
        Assert.Equal(6, answer.Citations[0].End);
    }
    [Fact] public async Task MissingAiUsesExplicitExtractiveMode()
    {
        var chat = new VideoChat(new FakeIntelligence(new("", []), false), new QuotaStore());
        var answer = await chat.AskAsync(ReadyVideo(), new("Bütçe?"), CancellationToken.None);
        Assert.Equal("extractive", answer.Mode); Assert.Single(answer.Citations);
    }
    [Fact] public async Task DailyQuotaStopsProviderCall()
    {
        var chat = new VideoChat(new FakeIntelligence(new("", [])), new QuotaStore(false));
        var exception = await Assert.ThrowsAsync<AppException>(() => chat.AskAsync(ReadyVideo(), new("Bütçe?"), CancellationToken.None));
        Assert.Equal(429, exception.Status);
    }
    [Fact] public async Task OpenAiAdapterUsesJsonEvidenceAndBearerHeader()
    {
        var handler = new StubHandler("{\"choices\":[{\"message\":{\"content\":\"{\\\"answer\\\":\\\"Yüz bin lira\\\",\\\"segmentIds\\\":[1]}\"}}]}");
        var services = ProviderTestServices.Create(handler);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<VideoIntelligence>();
        var response = await client.AnswerAsync(ReadyVideo().Segments, new("Bütçe?"), CancellationToken.None);
        Assert.Equal([1], response.SegmentIds);
        Assert.Contains("transcript evidence", handler.Body);
        Assert.Contains("segmentIds", handler.Body);
        Assert.Equal("Bearer", handler.AuthScheme);
        Assert.EndsWith("/chat/completions", handler.Path);
    }
    [Fact] public async Task TopicAdapterValidatesAndResolvesChapterBoundaries()
    {
        var inner = JsonSerializer.Serialize(new { chapters = new[] { new { title = "Plan", summary = "Lansman", category = "Strateji", firstSegmentId = 0 }, new { title = "Bütçe", summary = "Maliyet", category = "Finans", firstSegmentId = 1 } } });
        var handler = new StubHandler(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = inner } } } }));
        var services = ProviderTestServices.Create(handler);
        await using var provider = services.BuildServiceProvider();
        var topics = await provider.GetRequiredService<VideoIntelligence>().CategorizeAsync(ReadyVideo().Segments, CancellationToken.None);
        Assert.Equal("ai", topics.Mode); Assert.Equal(2, topics.Chapters.Count);
        Assert.Equal(3, topics.Chapters[0].End); Assert.Equal(9, topics.Chapters[1].End);
    }
    [Fact] public async Task TranscriptionAppliesChunkOffset()
    {
        var path = Path.GetTempFileName();
        try
        {
            var handler = new StubHandler("{\"segments\":[{\"start\":1,\"end\":3,\"text\":\"Merhaba\"}]}");
            var services = ProviderTestServices.Create(handler);
            await using var provider = services.BuildServiceProvider();
            var result = await provider.GetRequiredService<VideoIntelligence>().TranscribeAsync(path, 480, CancellationToken.None);
            Assert.Equal(481, result[0].Start); Assert.Equal(483, result[0].End);
            Assert.Contains("verbose_json", handler.Body); Assert.EndsWith("/audio/transcriptions", handler.Path);
        }
        finally { File.Delete(path); }
    }
    private static Video ReadyVideo()
    {
        var video = new Video(Guid.NewGuid(), "owner", "Synthetic", 100, TimeProvider.System.GetUtcNow());
        video.Complete(Subtitles.Parse(CoreTests.Transcript, 10), [], "ai"); return video;
    }
    private sealed class FakeIntelligence(AiAnswer response, bool configured = true) : IVideoIntelligence
    {
        public Task<bool> IsConfiguredAsync(CancellationToken ct) => Task.FromResult(configured);
        public Task<AiAnswer> AnswerAsync(IReadOnlyList<TranscriptSegment> evidence, ChatRequest request, CancellationToken ct) => Task.FromResult(response);
        public Task<TopicResult> CategorizeAsync(IReadOnlyList<TranscriptSegment> segments, CancellationToken ct) => Task.FromResult(new TopicResult([], "ai"));
    }
    private sealed class QuotaStore(bool allowed = true) : ILibraryStore
    {
        public Task<long> UsedBytesAsync(string owner, CancellationToken ct) => Task.FromResult(0L);
        public Task DeleteAsync(Video video, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ReserveChatAsync(Guid id, int limit, CancellationToken ct) => Task.FromResult(allowed);
    }
    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        public string? AuthScheme { get; private set; }
        public string? Path { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct); AuthScheme = request.Headers.Authorization?.Scheme; Path = request.RequestUri!.AbsoluteUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
