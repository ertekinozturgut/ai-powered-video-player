using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using VideoAtlas.Web;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class OperationsTests
{
    [Fact] public void CancellationKeepsCheckpointsAndCanBeResumed()
    {
        var v = Create(); v.StartAttempt(); v.Checkpoint(new(0, 480, "fingerprint", [new(0, 0, 3, "Korunan konuşma")]));
        v.RequestCancellation(); v.Cancel(); Assert.Equal(VideoStatus.Canceled, v.Status);
        v.Requeue(); v.StartAttempt(); Assert.False(v.CancelRequested); Assert.Single(v.TranscriptChunks); Assert.Equal(2, v.ProcessingAttempts);
        using var registry = new CancellationTokenSource(); var jobs = new VideoJobRegistry(); jobs.Register(v.Id, registry);
        jobs.Cancel(v.Id); Assert.True(registry.IsCancellationRequested); jobs.Remove(v.Id);
    }
    [Fact] public void SilenceMetadataCreatesWarningsWithoutRewritingText()
    {
        var intervals = MediaProcessor.ParseSilence("lavfi.silence_start=0\nlavfi.silence_end=4\nlavfi.silence_start=8", 10);
        Assert.Equal(new[] { new SilenceInterval(0, 4), new SilenceInterval(8, 10) }, intervals);
        var segments = new[] { new TranscriptSegment(0, 0, 3, "Şüpheli metin"), new TranscriptSegment(1, 4, 7, "Duyulan metin") };
        var issue = Assert.Single(TranscriptQuality.SilenceIssues(segments, intervals));
        Assert.Equal(0, issue.SegmentId); Assert.Null(issue.Suggestion); Assert.Equal("silence", issue.Source);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ClaimsMustEachReferenceEvidenceAndEmptyClaimsFailClosed(bool invalid)
    {
        var ai = new ClaimAi { Result = new("Untrusted prose", [0], invalid ? [new("Unsupported", [999])] : []) };
        var result = await new VideoChat(ai, new LibraryStub()).AskAsync(Create(), new("Konu?"), default);
        Assert.Empty(result.Citations); Assert.DoesNotContain("Untrusted prose", result.Answer); Assert.Null(result.Claims);
    }
    [Fact] public async Task ClaimTextReplacesUntrustedTopLevelProseAndCarriesSources()
    {
        var ai = new ClaimAi { Result = new("UNSOURCED", [999], [new("Kaynaklı iddia", [1])], "Belirsiz ayrıntı") };
        var result = await new VideoChat(ai, new LibraryStub()).AskAsync(Create(), new("Bütçe?"), default);
        Assert.Equal("Kaynaklı iddia", result.Answer); Assert.Equal(1, Assert.Single(Assert.Single(result.Claims!).Citations).SegmentId);
        Assert.Contains("Belirsiz", result.Warning);
    }
    [Fact] public async Task SummarySamplesLateRegionsAndSectionRestrictsEvidence()
    {
        var ai = new ClaimAi(); var v = Create(); v.SetMedia(1000);
        v.Complete(Enumerable.Range(0, 200).Select(i => new TranscriptSegment(i, i * 5, i * 5 + 4, new string('a', 400))).ToArray(), [], "ai");
        var chat = new VideoChat(ai, new LibraryStub());
        await chat.AskAsync(v, new("Özet", Action:"summary"), default);
        Assert.Contains(ai.Evidence, s => s.Start > 950); Assert.True(ai.Evidence.Sum(s => s.Text.Length) <= 48000);
        await chat.AskAsync(v, new("Bölüm", Action:"section", Start:900, End:920), default);
        Assert.All(ai.Evidence, s => Assert.InRange(s.Start, 900, 919));
    }
    [Fact] public async Task UsageReservesBeforeCallsRetainsUncertainCostAndEnforcesLimits()
    {
        await using var app = new PausedVideoAppFactory(); using var client = app.CreateClient();
        var v = Create();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Videos.Add(v); await db.SaveChangesAsync(); }
        using var recorder = new AiUsageRecorder(app.Services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new UsageOptions { MaxCallsPerVideoDay = 2 }), TimeProvider.System, NullLogger<AiUsageRecorder>.Instance);
        var context = new AiUsageContext(); context.SetVideo(v); context.SetOperation("chat");
        var connection = new ProviderConnection("test", "openrouter", "https://openrouter.ai/api/v1/", "openai/gpt-4.1-mini", 0, "synthetic");
        using var content = JsonContent.Create(new { question = "Synthetic" });
        var first = await recorder.ReserveAsync(context, connection, "chat/completions", content, default);
        using var reply = JsonDocument.Parse("{\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20,\"cost\":0.003}}");
        await recorder.FinishAsync(first, reply.RootElement, "success", null);
        var second = await recorder.ReserveAsync(context, new("test", "openrouter", connection.Endpoint, "unknown-model", 0, "synthetic"), "chat/completions", content, default);
        await recorder.FinishAsync(second, null, "uncertain", "connection_or_cancel");
        var error = await Assert.ThrowsAsync<AppException>(() => recorder.ReserveAsync(context, connection, "chat/completions", content, default));
        Assert.Equal("ai_budget", error.Code);
        using var read = app.Services.CreateScope();
        var rows = await read.ServiceProvider.GetRequiredService<AppDbContext>().AiUsage.Where(u => u.VideoId == v.Id).ToListAsync();
        var summary = AiUsageRecorder.Summarize(rows);
        Assert.Equal(2, summary.Calls); Assert.Equal(1, summary.UnpricedCalls); Assert.Equal(1, summary.FailedOrUncertain);
        Assert.Equal(.003, summary.ReportedUsd); Assert.True(rows.Single(r => r.Status == "success").TokensReported);
        var readDb = read.ServiceProvider.GetRequiredService<AppDbContext>();
        await readDb.Videos.Where(video => video.Id == v.Id).ExecuteDeleteAsync();
        Assert.Equal(2, await readDb.AiUsage.CountAsync(u => u.VideoId == v.Id));
        Assert.Equal("ai_budget", (await Assert.ThrowsAsync<AppException>(() => recorder.ReserveAsync(context, connection, "chat/completions", content, default))).Code);
    }
    private static Video Create()
    {
        var v = new Video(Guid.NewGuid(), "owner", "Test", 1, TimeProvider.System.GetUtcNow()); v.SetMedia(10);
        v.RecordTranscript(Subtitles.Parse(CoreTests.Transcript, 10), TimeProvider.System.GetUtcNow(), "test"); v.Complete(v.Segments, [], "ai"); return v;
    }
    private sealed class ClaimAi : IVideoIntelligence
    {
        public AiAnswer Result { get; set; } = new("", [], []);
        public IReadOnlyList<TranscriptSegment> Evidence { get; private set; } = [];
        public Task<bool> IsConfiguredAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<TopicResult> CategorizeAsync(IReadOnlyList<TranscriptSegment> s, CancellationToken ct) => Task.FromResult(new TopicResult([], "ai"));
        public Task<AiAnswer> AnswerAsync(IReadOnlyList<TranscriptSegment> s, ChatRequest r, CancellationToken ct) { Evidence = s; return Task.FromResult(Result); }
    }
    private sealed class LibraryStub : ILibraryStore
    {
        public Task<long> UsedBytesAsync(string o, CancellationToken ct) => Task.FromResult(0L);
        public Task DeleteAsync(Video v, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ReserveChatAsync(Guid id, int limit, CancellationToken ct) => Task.FromResult(true);
    }
}

public sealed partial class WebTests
{
    [Fact] public async Task SharePresentationPrivateAccessUsageAndRetentionAreEnforcedServerSide()
    {
        using var owner = await RegisterAsync(); using var intruder = await RegisterAsync();
        var fixture = await factory.FixtureAsync(); using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(fixture)), "file", "operations.mp4");
        upload.Add(new StringContent("Operations fixture"), "title"); upload.Add(new StringContent("true"), "consent");
        var response = await owner.PostAsync("/api/videos", upload); response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<VideoSummary>(VideoAppFactory.Json))!.Id; var path = $"/api/videos/{id}";
        await WaitUntilIdleAsync(owner, path); (await owner.PostAsJsonAsync(path + "/subtitles", new {content=CoreTests.Transcript})).EnsureSuccessStatusCode();
        var state = (await owner.GetFromJsonAsync<VideoOperationsView>(path + "/operations"))!;
        Assert.Equal(0, state.Usage.Calls); Assert.Null(state.DeleteAfter);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync(path + "/operations")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path + "/operations/retention", new RetentionCommand(state.Version, 1, false))).StatusCode);
        (await owner.PostAsJsonAsync(path + "/operations/retention", new RetentionCommand(state.Version, 1, true))).EnsureSuccessStatusCode();
        state = (await owner.GetFromJsonAsync<VideoOperationsView>(path + "/operations"))!; Assert.NotNull(state.DeleteAfter);
        var shared = await owner.PostAsJsonAsync(path + "/share", new { days=7, origins=new[]{"*"}, presentation=new SharePresentation("minimal", 2, false, true, false) });
        shared.EnsureSuccessStatusCode(); var token = (await shared.Content.ReadFromJsonAsync<ShareResult>())!.Path.Split('/')[^1];
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        var detail = (await anonymous.GetFromJsonAsync<VideoDetail>("/api/shared/" + token, VideoAppFactory.Json))!;
        Assert.Empty(detail.Segments); Assert.Equal("minimal", detail.Presentation!.Theme);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shared/{token}/subtitles.srt")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync($"/api/shared/{token}/chat", new ChatRequest("Bütçe?"))).StatusCode);
        string ownerEmail;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var v = await db.Videos.FindAsync(id);
            ownerEmail = (await db.Users.FindAsync(v!.OwnerId))!.Email!;
        }
        shared = await owner.PostAsJsonAsync(path + "/share", new {days=7, origins=new[]{"*"}, requireLogin=true, viewers=new[]{ownerEmail}});
        shared.EnsureSuccessStatusCode(); var link = (await shared.Content.ReadFromJsonAsync<ShareResult>())!.Path; token = link.Split('/')[^1];
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/shared/" + token)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync("/api/shared/" + token)).StatusCode);
        var page = await owner.GetAsync(link); page.EnsureSuccessStatusCode(); Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/shared/{token}/chat", new ChatRequest("Bütçe?"))).StatusCode);
        await SetCsrfAsync(owner, link);
        (await owner.PostAsJsonAsync($"/api/shared/{token}/chat", new ChatRequest("Bütçe?"))).EnsureSuccessStatusCode();
        (await owner.DeleteAsync(path)).EnsureSuccessStatusCode();
    }
}
