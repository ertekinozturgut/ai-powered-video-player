using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class PipelineRecoveryTests
{
    [Fact] public async Task FailedSecondAudioChunkResumesWithoutPayingForFirstAgain()
    {
        var handler = new ChunkHandler(); await using var app = new CheckpointFactory(handler); using var client = app.CreateClient();
        var fixture = await app.FixtureAsync();
        // Long duration, low frame-rate synthetic media exercises the actual 480-second boundary.
        var longFixture = Path.Combine(Path.GetDirectoryName(fixture)!, "long.mp4");
        await Ffmpeg("-f", "lavfi", "-i", "color=size=160x90:rate=1", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000", "-t", "490", "-c:v", "libx264", "-threads", "1", "-c:a", "aac", longFixture);
        Guid id;
        using (var scope = app.Services.CreateScope())
        {
            await using var stream = File.OpenRead(longFixture);
            id = (await scope.ServiceProvider.GetRequiredService<VideoLibrary>().UploadAsync(new("owner", "Checkpoint", stream.Length, true, stream), default)).Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var v = await db.Videos.FindAsync(id); v!.StartAttempt();
            await Assert.ThrowsAsync<AiException>(() => scope.ServiceProvider.GetRequiredService<VideoPipeline>().RunAsync(v, default));
        }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var v = await db.Videos.FindAsync(id);
            Assert.Single(v!.TranscriptChunks); Assert.Empty(v.Segments); Assert.Equal(2, handler.SpeechCalls);
            v.Requeue(); v.StartAttempt();
            await scope.ServiceProvider.GetRequiredService<VideoPipeline>().RunAsync(v, default); await db.SaveChangesAsync();
            Assert.Equal(VideoStatus.Ready, v.Status); Assert.Equal(2, v.TranscriptChunks.Count); Assert.Equal(3, handler.SpeechCalls);
            Assert.Contains(v.Segments, s => s.Start >= 480); Assert.True(v.SilenceChecked);
        }
    }
    [Fact] public async Task QualityBatchesResumeAtSavedCursorAndSilenceIsActuallyDetected()
    {
        var reviewer = new BatchReviewer(); await using var app = new CheckpointFactory(new ControlledProvider(), reviewer); using var client = app.CreateClient();
        var fixture = await app.FixtureAsync();
        var silent = Path.Combine(Path.GetDirectoryName(fixture)!, "silent.mp4");
        await Ffmpeg("-f", "lavfi", "-i", "color=size=160x90:rate=1", "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono", "-t", "10", "-c:v", "libx264", "-threads", "1", "-c:a", "aac", silent);
        Guid id;
        using (var scope = app.Services.CreateScope())
        {
            await using var stream = File.OpenRead(silent); id = (await scope.ServiceProvider.GetRequiredService<VideoLibrary>().UploadAsync(new("owner", "Quality", stream.Length, true, stream), default)).Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var v = (await db.Videos.FindAsync(id))!;
            v.RecordTranscript(Enumerable.Range(0, 60).Select(i => new TranscriptSegment(i, i / 10d, (i + 1) / 10d, "Metin")), TimeProvider.System.GetUtcNow(), "fixture");
            v.ConfigureFullReview(true); v.StartAttempt();
            await Assert.ThrowsAsync<AiException>(() => scope.ServiceProvider.GetRequiredService<VideoPipeline>().RunAsync(v, default));
        }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var v = (await db.Videos.FindAsync(id))!;
            Assert.Equal(40, v.QualityCursor); Assert.Contains(v.TranscriptIssues, i => i.Source == "silence"); Assert.True(v.SilenceChecked);
            v.Requeue(); v.StartAttempt(); await scope.ServiceProvider.GetRequiredService<VideoPipeline>().RunAsync(v, default); await db.SaveChangesAsync();
            Assert.Equal(60, v.QualityCursor); Assert.Equal(3, reviewer.Calls); Assert.Equal(39, reviewer.FirstIds[^1]); Assert.Equal(VideoStatus.Ready, v.Status);
            Assert.Equal("Metin", v.Segments[0].Text);
            Assert.Contains(v.TranscriptIssues, issue => issue.Source == "llm-batch" && issue.SegmentId == 0);
        }
    }
    [Fact] public async Task WorkerCancelsInFlightProviderAndDeletesOnlyExplicitlyExpiredVideos()
    {
        var handler = new BlockingHandler(); await using var app = new CheckpointFactory(handler); using var client = app.CreateClient();
        Guid id, expiredId;
        using (var scope = app.Services.CreateScope())
        {
            var fixture = await app.FixtureAsync(); await using var stream = File.OpenRead(fixture);
            id = (await scope.ServiceProvider.GetRequiredService<VideoLibrary>().UploadAsync(new("owner", "Cancel", stream.Length, true, stream), default)).Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expired = new Video(Guid.NewGuid(), "owner", "Expired", 1, TimeProvider.System.GetUtcNow()); expired.Complete([], [], "keywords"); expired.SetRetention(TimeProvider.System.GetUtcNow().AddDays(-1));
            expiredId = expired.Id; db.Videos.Add(expired); await db.SaveChangesAsync();
        }
        using var worker = new VideoWorker(app.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<VideoWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            app.Services.GetRequiredService<VideoJobRegistry>().Cancel(id);
            for (var i = 0; i < 100; i++)
            {
                using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var v = (await db.Videos.FindAsync(id))!;
                if (v.Status == VideoStatus.Canceled) { Assert.Null(await db.Videos.FindAsync(expiredId)); Assert.Null(v.DeleteAfter); return; }
                await Task.Delay(100);
            }
            Assert.Fail("Worker did not cancel.");
        }
        finally { await worker.StopAsync(default); }
    }
    private static async Task Ffmpeg(params string[] args)
    {
        var info = new ProcessStartInfo("ffmpeg") { UseShellExecute=false, RedirectStandardError=true };
        foreach (var arg in new[] { "-nostdin", "-y", "-v", "error" }.Concat(args)) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await error);
    }
    private sealed class ChunkHandler : DelegatingHandler
    {
        public int SpeechCalls;
        public ChunkHandler() : base(new ControlledProvider()) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/transcriptions", StringComparison.Ordinal) && ++SpeechCalls == 2) throw new AiException("ai_unavailable");
            return base.SendAsync(request, ct);
        }
    }
    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); }
    }
    private sealed class BatchReviewer : ITranscriptReviewer
    {
        public int Calls; public List<int> FirstIds { get; } = [];
        public Task<IReadOnlyList<TranscriptIssue>> InspectBatchAsync(IReadOnlyList<TranscriptSegment> segments, VideoContext context, CancellationToken ct)
        {
            Calls++; FirstIds.Add(segments[0].Id);
            if (Calls == 2) throw new AiException("ai_unavailable");
            return Task.FromResult<IReadOnlyList<TranscriptIssue>>(Calls == 1
                ? [new(Guid.NewGuid(), segments[0].Id, "Metni sesle doğrulayın", "Spekülatif değişiklik", "llm-batch")]
                : []);
        }
        public Task<TranscriptIssue?> InspectAsync(IReadOnlyList<TranscriptSegment> s, int id, VideoContext c, CancellationToken ct) => Task.FromResult<TranscriptIssue?>(null);
        public Task<TranscriptIssue> RetranscribeAsync(Guid id, double duration, TranscriptSegment s, CancellationToken ct) => throw new NotSupportedException();
    }
}
internal sealed class CheckpointFactory(HttpMessageHandler handler, ITranscriptReviewer? reviewer = null) : VideoAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(VideoWorker)));
            services.PostConfigure<AiOptions>(o => o.ApiKey = "synthetic-checkpoint-key");
            services.AddHttpClient("AiProviders").ConfigurePrimaryHttpMessageHandler(() => handler);
            if (reviewer is not null) { services.RemoveAll<ITranscriptReviewer>(); services.AddSingleton(reviewer); }
        });
    }
}
