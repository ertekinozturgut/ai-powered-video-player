using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Web;

[ApiController]
public sealed class PlaybackController(VideoAccess access, IMediaStorage files, VideoChat chat, TimeProvider clock, IVideoIntelligence intelligence) : ControllerBase
{
    [Authorize, HttpGet("api/videos/{id:guid}/{asset}")]
    public async Task<IActionResult> OwnedAsset(Guid id, string asset, CancellationToken ct) =>
        Asset(await access.OwnedAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct), asset);
    [Authorize, HttpPost("api/videos/{id:guid}/chat"), RequestSizeLimit(65536), EnableRateLimiting("chat")]
    public async Task<IActionResult> OwnedChat(Guid id, ChatRequest request, CancellationToken ct) =>
        Ok(await chat.AskAsync(await access.OwnedAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct), request, ct));

    // Public capabilities use tokens; restricted shares additionally require an allowed account.
    [AllowAnonymous, HttpGet("api/shared/{token}")]
    public async Task<IActionResult> SharedDetail(string token, CancellationToken ct)
    {
        var video = await access.SharedAsync(token, ct, User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(VideoMapping.Detail(video, clock.GetUtcNow()) with { IndexStatus = await intelligence.IndexStatusAsync(video, ct), AiConfigured = video.ShareChat && await intelligence.IsConfiguredAsync(ct),
            Presentation = video.Presentation, Segments = video.ShareTranscript ? video.Segments : [], Chapters = video.ShareChapters ? video.Chapters : [] });
    }
    [AllowAnonymous, HttpGet("api/shared/{token}/{asset}")]
    public async Task<IActionResult> SharedAsset(string token, string asset, CancellationToken ct)
    {
        var v = await access.SharedAsync(token, ct, User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (!v.ShareTranscript && asset is "subtitles.srt" or "subtitles.vtt") return NotFound();
        return Asset(v, asset);
    }
    // Public tokens do not use ambient authentication. Restricted shares validate CSRF explicitly.
    [AllowAnonymous, IgnoreAntiforgeryToken, HttpPost("api/shared/{token}/chat"), RequestSizeLimit(65536), EnableRateLimiting("chat")]
    public async Task<IActionResult> SharedChat(string token, ChatRequest request, CancellationToken ct)
    {
        var v = await access.SharedAsync(token, ct, User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (!v.ShareChat) return StatusCode(403, new { title = "Bu paylaşımda sohbet kapalı." });
        if (v.ShareRequiresLogin)
        {
            try { await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>().ValidateRequestAsync(HttpContext); }
            catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return BadRequest(new { title = "Güvenlik doğrulaması başarısız. Sayfayı yenileyin." }); }
        }
        return Ok(await chat.AskAsync(v, request, ct));
    }

    private IActionResult Asset(Video video, string asset)
    {
        if (asset is "subtitles.srt" or "subtitles.vtt")
        {
            VideoAccess.RequireReady(video);
            return File(Encoding.UTF8.GetBytes(Subtitles.Format(video.Segments, asset.EndsWith(".vtt", StringComparison.Ordinal), video.SpeakerNames)),
                asset == "subtitles.vtt" ? "text/vtt; charset=utf-8" : "application/x-subrip; charset=utf-8",
                asset == "subtitles.srt" ? "transcript.srt" : null);
        }
        if (!video.MediaReady || asset is not ("media" or "poster")) return NotFound();
        var path = files.PathFor(video.Id, asset == "media" ? "video.mp4" : "poster.jpg");
        return System.IO.File.Exists(path) ? PhysicalFile(path, asset == "media" ? "video/mp4" : "image/jpeg", enableRangeProcessing: true) : NotFound();
    }
}
