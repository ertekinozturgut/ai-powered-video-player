using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Application;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed partial class WebTests : IClassFixture<VideoAppFactory>
{
    private readonly VideoAppFactory factory;
    public WebTests(VideoAppFactory factory) => this.factory = factory;
    [GeneratedRegex("name=\"csrf-token\" content=\"([^\"]+)\"", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Csrf();

    [Fact] public async Task AnonymousRequestsCannotReadPrivateLibrary()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/videos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/shared/invalid-token")).StatusCode);
        var login = await client.GetAsync("/account/login");
        Assert.Contains("frame-ancestors 'none'", login.Headers.GetValues("Content-Security-Policy").Single());
    }
    [Fact] public async Task FullUploadSubtitlePlaybackShareAndIsolationFlow()
    {
        using var client = await RegisterAsync();
        using var intruder = await RegisterAsync();
        var fixture = await factory.FixtureAsync();
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(fixture)), "file", "../../synthetic.mp4");
        upload.Add(new StringContent("Sentetik ürün toplantısı"), "title");
        upload.Add(new StringContent("true"), "consent");
        var response = await client.PostAsync("/api/videos", upload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VideoSummary>(VideoAppFactory.Json);
        Assert.NotNull(created);
        var path = $"/api/videos/{created.Id}";
        VideoDetail? detail = null;
        for (var i = 0; i < 80; i++)
        {
            detail = await client.GetFromJsonAsync<VideoDetail>(path, VideoAppFactory.Json);
            if (detail!.Video.Status is not (Domain.VideoStatus.Processing or Domain.VideoStatus.Queued)) break;
            await Task.Delay(250);
        }
        Assert.NotNull(detail); Assert.Equal(Domain.VideoStatus.AwaitingTranscript, detail.Video.Status); Assert.True(detail.Video.MediaReady);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync(path + "/media")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsJsonAsync(path + "/share", new { days = 7, origins = new[] { "*" } })).StatusCode);
        var invalid = await client.PostAsJsonAsync(path + "/subtitles", new { content = "not srt" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var imported = await client.PostAsJsonAsync(path + "/subtitles", new { content = CoreTests.Transcript });
        Assert.Equal(HttpStatusCode.NoContent, imported.StatusCode);
        detail = await client.GetFromJsonAsync<VideoDetail>(path, VideoAppFactory.Json);
        Assert.Equal(Domain.VideoStatus.Ready, detail!.Video.Status); Assert.Equal(3, detail.Segments.Count); Assert.NotEmpty(detail.Chapters);
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, path + "/media");
        rangeRequest.Headers.Range = new(0, 127);
        var media = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, media.StatusCode); Assert.Equal(128, (await media.Content.ReadAsByteArrayAsync()).Length);
        var srt = await client.GetStringAsync(path + "/subtitles.srt"); Assert.Contains("00:00:03,000", srt);
        var answerResponse = await client.PostAsJsonAsync(path + "/chat", new ChatRequest("Bütçe ne kadar?"));
        answerResponse.EnsureSuccessStatusCode();
        var answer = await answerResponse.Content.ReadFromJsonAsync<ChatAnswer>(); Assert.Equal(3, answer!.Citations[0].Start);
        var share = await client.PostAsJsonAsync(path + "/share", new { days = 7, origins = new[] { "https://example.com" } });
        share.EnsureSuccessStatusCode();
        var sharePath = (await share.Content.ReadFromJsonAsync<ShareResult>())!.Path;
        var token = sharePath.Split('/')[^1];
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        var embed = await anonymous.GetAsync(sharePath); Assert.Equal(HttpStatusCode.OK, embed.StatusCode);
        Assert.Contains("frame-ancestors https://example.com", embed.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(embed.Headers.Contains("X-Frame-Options"));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/shared/{token}/media")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync($"/api/shared/{token}/chat", new ChatRequest("Bütçe?"))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Videos.FindAsync(created.Id); Assert.Equal(VideoAccess.Hash(token), stored!.ShareHash); Assert.NotEqual(token, stored.ShareHash);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path + "/share")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shared/{token}/media")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(sharePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync($"/api/shared/{token}/chat", new ChatRequest("Bütçe?"))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync(path)).StatusCode);
        await SetCsrfAsync(client, "/");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        using var finalScope = factory.Services.CreateScope();
        var files = finalScope.ServiceProvider.GetRequiredService<IMediaStorage>(); Assert.False(File.Exists(files.PathFor(created.Id, "video.mp4")));
    }

    [Fact] public async Task AudioUploadIsPreparedForPlaybackAndTranscriptAnalysis()
    {
        using var client = await RegisterAsync();
        var fixture = await factory.AudioFixtureAsync();
        using var upload = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(await File.ReadAllBytesAsync(fixture));
        bytes.Headers.ContentType = new("audio/mpeg");
        upload.Add(bytes, "file", "synthetic.mp3");
        upload.Add(new StringContent("Sentetik ses kaydı"), "title");
        upload.Add(new StringContent("true"), "consent");
        var response = await client.PostAsync("/api/videos", upload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VideoSummary>(VideoAppFactory.Json);
        Assert.NotNull(created);
        VideoDetail? detail = null;
        for (var i = 0; i < 80; i++)
        {
            detail = await client.GetFromJsonAsync<VideoDetail>($"/api/videos/{created.Id}", VideoAppFactory.Json);
            if (detail!.Video.Status is not (Domain.VideoStatus.Processing or Domain.VideoStatus.Queued)) break;
            await Task.Delay(250);
        }
        Assert.NotNull(detail);
        Assert.Equal("audio", detail.Video.MediaKind);
        Assert.True(detail.Video.MediaReady);
        Assert.Equal(Domain.VideoStatus.AwaitingTranscript, detail.Video.Status);
        Assert.Equal("video/mp4", (await client.GetAsync($"/api/videos/{created.Id}/media")).Content.Headers.ContentType?.MediaType);
        Assert.Equal("image/jpeg", (await client.GetAsync($"/api/videos/{created.Id}/poster")).Content.Headers.ContentType?.MediaType);
    }

    private async Task<HttpClient> RegisterAsync()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var token = await SetCsrfAsync(client, "/account/register");
        var response = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Email"] = $"test-{Guid.NewGuid():N}@example.invalid", ["Password"] = "Synthetic-Test!234", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await SetCsrfAsync(client, "/");
        return client;
    }
    private static async Task<string> SetCsrfAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var token = Csrf().Match(html).Groups[1].Value; Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token); return token;
    }
    private sealed record ShareResult(string Path);
}

public class VideoAppFactory : WebApplicationFactory<Program>
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "videoatlas-tests-" + Guid.NewGuid().ToString("N"));
    public static System.Text.Json.JsonSerializerOptions Json { get; } = new(System.Text.Json.JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:Root", root);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Storage:Root"] = root, ["Accounts:AllowRegistration"] = "true", ["Ai:ApiKey"] = "",
          ["ProviderCredentials:OpenRouter"] = "", ["ProviderCredentials:ElevenLabs"] = "" }));
    }
    public async Task<string> FixtureAsync()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "synthetic.mp4");
        var info = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in new[] { "-nostdin", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000", "-t", "10", "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p", "-c:a", "aac", file }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await error); return file;
    }
    public async Task<string> AudioFixtureAsync()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "synthetic.mp3");
        var info = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in new[] { "-nostdin", "-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000", "-t", "5", "-c:a", "libmp3lame", "-b:a", "64k", file }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await error); return file;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(root)) Directory.Delete(root, true);
    }
}
