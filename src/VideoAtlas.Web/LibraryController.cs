using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VideoAtlas.Application;

namespace VideoAtlas.Web;

[Authorize, ApiController, Route("api/videos")]
public sealed class LibraryController(IVideoStore store, VideoLibrary library, VideoAccess access, TimeProvider clock, IVideoIntelligence intelligence, IShareAudience audience) : ControllerBase
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 0, CancellationToken ct = default) => Ok(await store.ListAsync(Owner, page, ct));
    [HttpPost, EnableRateLimiting("upload"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Upload([FromForm] UploadForm form, CancellationToken ct)
    {
        await using var stream = form.File.OpenReadStream();
        var terms = (form.Terms ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var pair = line.Split('|', 2);
            return new VideoAtlas.Domain.VideoTerm(pair[0], pair.Length == 2 ? pair[1] : "");
        }).ToArray();
        var context = TranscriptQuality.ValidateContext(terms, form.AnalysisInstructions ?? "", form.AnalysisLanguage);
        var video = await library.UploadAsync(new(Owner, form.Title, form.File.Length, form.Consent, stream, context, form.FullReview), ct);
        return Created($"/api/videos/{video.Id}", video);
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var video = await access.OwnedAsync(id, Owner, ct);
        return Ok(VideoMapping.Detail(video, clock.GetUtcNow()) with { IndexStatus = await intelligence.IndexStatusAsync(video, ct), AiConfigured = await intelligence.IsConfiguredAsync(ct) });
    }
    [HttpDelete("{id:guid}"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await library.DeleteAsync(await access.OwnedAsync(id, Owner, ct), ct); return NoContent(); }
    [HttpPost("{id:guid}/retry"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct) { await library.RetryAsync(await access.OwnedAsync(id, Owner, ct), ct); return Accepted(); }
    [HttpPost("{id:guid}/subtitles"), RequestSizeLimit(2_100_000), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Import(Guid id, SubtitleForm form, CancellationToken ct)
    { await library.ImportAsync(await access.OwnedAsync(id, Owner, ct), form.Content, ct); return NoContent(); }
    [HttpPost("{id:guid}/share"), RequestSizeLimit(16384), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Share(Guid id, ShareForm form, CancellationToken ct)
    {
        var token = await library.ShareAsync(await access.OwnedAsync(id, Owner, ct), new(form.Days, form.Origins, form.Presentation, form.RequireLogin, form.Viewers), ct);
        return Ok(new { path = $"/embed/{token}", requireLogin = form.RequireLogin });
    }
    [HttpGet("{id:guid}/share/audience")]
    public async Task<IActionResult> ShareAudience(Guid id, CancellationToken ct)
    {
        _ = await access.OwnedAsync(id, Owner, ct);
        return Ok(await audience.ListAsync(ct));
    }
    [HttpDelete("{id:guid}/share"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    { await library.RevokeAsync(await access.OwnedAsync(id, Owner, ct), ct); return NoContent(); }
}
