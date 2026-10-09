using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VideoAtlas.Application;

namespace VideoAtlas.Web;

public sealed class HomeController(VideoAccess access, IVideoIntelligence intelligence) : Controller
{
    [Authorize, HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.AiConfigured = await intelligence.IsConfiguredAsync(ct);
        ViewBag.SpeechConfigured = await intelligence.SpeechConfiguredAsync(ct);
        return View();
    }
    [Authorize, HttpGet("videos/{id:guid}")]
    public async Task<IActionResult> Player(Guid id, CancellationToken ct) { ViewBag.AiConfigured = await intelligence.IsConfiguredAsync(ct); return View(new PlayerModel($"/api/videos/{id}", false)); }
    // An owner has explicitly published this capability URL. Validate on every request including media and chat.
    [AllowAnonymous, HttpGet("embed/{token}")]
    public async Task<IActionResult> Embed(string token, CancellationToken ct)
    {
        VideoAtlas.Domain.Video video;
        try { video = await access.SharedAsync(token, ct, User.FindFirstValue(ClaimTypes.NameIdentifier)); }
        catch (AppException ex) when (ex.Status == 401) { return Redirect("/account/login?returnUrl=" + Uri.EscapeDataString($"/embed/{token}")); }
        Response.Headers.Remove("X-Frame-Options");
        Response.Headers.ContentSecurityPolicy = SecurityHeaders.Policy(video.FrameOrigins);
        ViewBag.AiConfigured = await intelligence.IsConfiguredAsync(ct);
        ViewData["PrivateShare"] = video.ShareRequiresLogin;
        return View("Player", new PlayerModel($"/api/shared/{token}", true));
    }
    [AllowAnonymous, HttpGet("privacy")]
    public IActionResult Privacy() => View();
}
