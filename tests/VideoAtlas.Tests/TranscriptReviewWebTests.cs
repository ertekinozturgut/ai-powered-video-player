using System.Net;
using System.Net.Http.Json;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using Xunit;

namespace VideoAtlas.Tests;

public sealed partial class WebTests
{
    [Fact] public async Task OwnerContextReviewCorrectionRestoreAndSharedPrivacyFlow()
    {
        using var owner = await RegisterAsync(); using var intruder = await RegisterAsync();
        var fixture = await factory.FixtureAsync();
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(fixture)), "file", "review.mp4");
        upload.Add(new StringContent("Kalite incelemesi"), "title"); upload.Add(new StringContent("true"), "consent");
        upload.Add(new StringContent("VideoAtlas | Ürün adı"), "terms");
        upload.Add(new StringContent("Riskleri öne çıkar."), "analysisInstructions");
        var uploaded = await owner.PostAsync("/api/videos", upload); uploaded.EnsureSuccessStatusCode();
        var id = (await uploaded.Content.ReadFromJsonAsync<VideoSummary>(VideoAppFactory.Json))!.Id;
        var path = $"/api/videos/{id}"; await WaitUntilIdleAsync(owner, path);
        (await owner.PostAsJsonAsync(path + "/subtitles", new { content = CoreTests.Transcript.Replace("Ürün", "VideoAtls", StringComparison.Ordinal) })).EnsureSuccessStatusCode();
        var state = (await owner.GetFromJsonAsync<ReviewState>(path + "/review"))!;
        Assert.Equal("VideoAtlas", state.Context.Terms[0].Term); Assert.Equal("Riskleri öne çıkar.", state.Context.Instructions);
        Assert.Single(state.Issues); Assert.Equal("heuristic", state.Issues[0].Source);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync(path + "/review")).StatusCode);
        foreach (var operation in new[] { "check", "segments/0/ai", "segments/0/retranscribe", "restore/-1" })
            Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsJsonAsync(path + "/review/" + operation, new ReviewCommand(state.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsJsonAsync(path + "/review/context", new ContextCommand(state.Version, [], ""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsJsonAsync(path + "/review/edit", new EditTranscriptCommand(state.Version, 0, "Unauthorized", true))).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path + "/review/check", new ReviewCommand(state.Version))).StatusCode);
        await SetCsrfAsync(owner, "/");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path + "/review/edit", new EditTranscriptCommand(state.Version, 0, "VideoAtlas lansmanı Ekim ayında yapılacak.", false))).StatusCode);
        (await owner.PostAsJsonAsync(path + "/review/edit", new EditTranscriptCommand(state.Version, 0, "VideoAtlas lansmanı Ekim ayında yapılacak.", true))).EnsureSuccessStatusCode();
        var updated = await WaitUntilIdleAsync(owner, path); Assert.Equal(VideoStatus.Ready, updated.Video.Status);
        Assert.Equal(0, updated.Segments[0].Start); Assert.Equal(3, updated.Segments[0].End);
        Assert.Contains("VideoAtlas", await owner.GetStringAsync(path + "/subtitles.srt"));
        var stale = await owner.PostAsJsonAsync(path + "/review/edit", new EditTranscriptCommand(state.Version, 0, "Stale content", true));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        state = (await owner.GetFromJsonAsync<ReviewState>(path + "/review"))!;
        Assert.Contains("VideoAtls", state.OriginalSegments[0].Text); Assert.Equal(2, state.Revisions.Count);
        (await owner.PostAsJsonAsync(path + "/review/restore/-1", new ReviewCommand(state.Version))).EnsureSuccessStatusCode();
        await WaitUntilIdleAsync(owner, path);
        var answer = await (await owner.PostAsJsonAsync(path + "/chat", new ChatRequest("VideoAtls?"))).Content.ReadFromJsonAsync<ChatAnswer>();
        Assert.NotNull(answer!.Warning);
        Assert.Equal(3, answer.TranscriptVersion);
        state = (await owner.GetFromJsonAsync<ReviewState>(path + "/review"))!;
        (await owner.PostAsJsonAsync(path + "/review/context", new ContextCommand(state.Version, [new("ARR", "Yıllık gelir")], "Yalnızca riskleri grupla."))).EnsureSuccessStatusCode();
        await WaitUntilIdleAsync(owner, path);
        var saved = (await owner.GetFromJsonAsync<ReviewState>(path + "/review"))!;
        Assert.Equal("ARR", saved.Context.Terms[0].Term); Assert.Equal("Yalnızca riskleri grupla.", saved.Context.Instructions);
        var share = await owner.PostAsJsonAsync(path + "/share", new { days = 7, origins = new[] { "*" } }); share.EnsureSuccessStatusCode();
        var sharedPath = (await share.Content.ReadFromJsonAsync<ShareResult>())!.Path;
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        var shared = await anonymous.GetStringAsync("/api/shared/" + sharedPath.Split('/')[^1]);
        Assert.DoesNotContain("Yalnızca riskleri", shared); Assert.DoesNotContain("originalSegments", shared); Assert.DoesNotContain("transcriptHistory", shared);
        var html = await anonymous.GetStringAsync(sharedPath); Assert.DoesNotContain("review-dialog", html); Assert.DoesNotContain("context-dialog", html);
        (await owner.DeleteAsync(path)).EnsureSuccessStatusCode();
    }
    private static async Task<VideoDetail> WaitUntilIdleAsync(HttpClient client, string path)
    {
        for (var i = 0; i < 120; i++)
        {
            var detail = (await client.GetFromJsonAsync<VideoDetail>(path, VideoAppFactory.Json))!;
            if (detail.Video.Status is not (VideoStatus.Queued or VideoStatus.Processing)) return detail;
            await Task.Delay(150);
        }
        throw new TimeoutException("Controlled video job did not finish.");
    }
}
