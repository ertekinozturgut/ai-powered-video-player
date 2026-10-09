using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class RecoveryTests
{
    [Fact] public async Task WorkerRecoversInterruptedRowAndRejectsCorruptMediaWithoutServingIt()
    {
        await using var app = new PausedVideoAppFactory();
        using var client = app.CreateClient();
        var fixture = await app.FixtureAsync();
        Guid interruptedId, corruptId;
        using (var scope = app.Services.CreateScope())
        {
            var library = scope.ServiceProvider.GetRequiredService<VideoLibrary>();
            await using var content = File.OpenRead(fixture);
            interruptedId = (await library.UploadAsync(new("owner", "Interrupted", content.Length, true, content), CancellationToken.None)).Id;
            var store = scope.ServiceProvider.GetRequiredService<IVideoStore>();
            var video = await store.FindAsync(interruptedId, CancellationToken.None);
            video!.Begin(); await store.SaveAsync(CancellationToken.None);
            await using var invalid = new MemoryStream("not a video"u8.ToArray());
            corruptId = (await library.UploadAsync(new("owner", "Corrupt", invalid.Length, true, invalid), CancellationToken.None)).Id;
        }
        using var worker = new VideoWorker(app.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<VideoWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var recovered = await WaitAsync(app, interruptedId);
            Assert.Equal(VideoStatus.AwaitingTranscript, recovered.Status); Assert.True(recovered.MediaReady);
            var corrupt = await WaitAsync(app, corruptId);
            Assert.Equal(VideoStatus.Failed, corrupt.Status); Assert.False(corrupt.MediaReady); Assert.Equal("invalid_video", corrupt.ErrorCode);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }
    private static async Task<Video> WaitAsync(VideoAppFactory app, Guid id)
    {
        for (var i = 0; i < 100; i++)
        {
            using var scope = app.Services.CreateScope();
            var video = await scope.ServiceProvider.GetRequiredService<IVideoStore>().FindAsync(id, CancellationToken.None);
            if (!video!.IsBusy) return video;
            await Task.Delay(200);
        }
        throw new TimeoutException("Synthetic worker did not complete.");
    }
}
internal sealed class PausedVideoAppFactory : VideoAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            var worker = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(VideoWorker));
            services.Remove(worker);
        });
    }
}
