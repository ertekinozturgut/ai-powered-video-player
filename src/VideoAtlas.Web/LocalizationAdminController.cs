using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoAtlas.Application;

namespace VideoAtlas.Web;

[Authorize(Policy = "Administrator"), Route("admin/localization"), RequestSizeLimit(1_100_000)]
public sealed class LocalizationAdminController(ILocalizationAdministration catalog) : Controller
{
    [HttpGet] public async Task<IActionResult> Index(string? language, CancellationToken ct) => View(await catalog.ReadAsync(language, ct));
    [HttpPost("language")]
    public async Task<IActionResult> Language(LanguageForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Index", await catalog.ReadAsync(null, ct));
        try { await catalog.SaveLanguageAsync(form.Code, form.Name, form.Enabled, form.IsDefault, form.Version, User.Identity!.Name!, ct); TempData["LocalizationMessage"] = "Dil kaydedildi."; return RedirectToAction(nameof(Index), new { language = form.Code }); }
        catch (AppException ex) { ModelState.AddModelError("", ex.Message); return View("Index", await catalog.ReadAsync(form.Code, ct)); }
    }
    [HttpPost("translation")]
    public async Task<IActionResult> Translation(TranslationForm form, CancellationToken ct)
    {
        try { await catalog.SaveTranslationAsync(form.Language, form.Key, form.Text, form.Version, User.Identity!.Name!, ct); TempData["LocalizationMessage"] = "Metin kaydedildi."; }
        catch (AppException ex) { TempData["LocalizationMessage"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { language = form.Language });
    }
    [HttpPost("import")]
    public async Task<IActionResult> Import(ImportLanguageForm form, CancellationToken ct)
    {
        try { await catalog.ImportAsync(form.Json, User.Identity!.Name!, ct); TempData["LocalizationMessage"] = "Dil paketi içe aktarıldı."; }
        catch (Exception) { TempData["LocalizationMessage"] = "JSON paketi geçersiz. code, name ve translations alanlarını kontrol edin."; }
        return RedirectToAction(nameof(Index));
    }
    [HttpGet("export/{language}")]
    public async Task<IActionResult> Export(string language, CancellationToken ct) => File(System.Text.Encoding.UTF8.GetBytes(await catalog.ExportAsync(language, ct)), "application/json", $"videoatlas.{language}.json");
    [HttpPost("fill/{language}")]
    public async Task<IActionResult> Fill(string language, CancellationToken ct)
    {
        try { var count = await catalog.FillWithAiAsync(language, User.Identity!.Name!, ct); TempData["LocalizationMessage"] = count == 0 ? "Doldurulacak eksik metin yok." : $"AI {count} eksik metni taslak olarak doldurdu."; }
        catch (AppException ex) { TempData["LocalizationMessage"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { language });
    }
}

[Authorize, Route("language")]
public sealed class LanguageController(ILocalizationAdministration catalog, TimeProvider clock) : Controller
{
    private void SetLanguageCookie(string code) => Response.Cookies.Append("VideoAtlas.Language", code,
        new CookieOptions { IsEssential = true, HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, Expires = clock.GetUtcNow().AddYears(1), Path = "/" });

    [HttpPost("select")]
    public async Task<IActionResult> Select([FromForm, StringLength(12)] string code, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        code = (code ?? "").Trim().ToLowerInvariant();
        if (!await catalog.SetUserLanguageAsync(owner, code, ct)) return BadRequest("Seçilen arayüz dili kullanıma açık değil.");
        SetLanguageCookie(code);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    [HttpPost("preference"), AllowAnonymous]
    public async Task<IActionResult> Preference([FromForm, StringLength(12)] string code, CancellationToken ct)
    {
        code = (code ?? "").Trim().ToLowerInvariant();
        var enabled = await catalog.EnabledAsync(ct);
        if (!enabled.Any(x => x.Code == code)) return BadRequest("Seçilen arayüz dili kullanıma açık değil.");
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(owner) && !await catalog.SetUserLanguageAsync(owner, code, ct)) return BadRequest();
        SetLanguageCookie(code);
        return NoContent();
    }
}
[Route("api/localization")]
public sealed class LocalizationController(ILocalizationAdministration catalog) : ControllerBase
{
    [HttpGet, AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Vary = "Cookie";
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var language = await catalog.ResolveLanguageAsync(owner, Request.Cookies["VideoAtlas.Language"], ct);
        var supported = (await catalog.EnabledAsync(ct)).Select(x => x.Code).ToArray();
        return Ok(new { language, supported, texts = await catalog.ClientTextsAsync(language, ct) });
    }
}
public sealed class LanguageForm { [Required, StringLength(12)] public string Code { get; set; } = ""; [Required, StringLength(80)] public string Name { get; set; } = ""; public bool Enabled { get; set; } = true; public bool IsDefault { get; set; } public int Version { get; set; } }
public sealed class TranslationForm { [Required, StringLength(12)] public string Language { get; set; } = ""; [Required, StringLength(120)] public string Key { get; set; } = ""; [Required, StringLength(2000)] public string Text { get; set; } = ""; public int Version { get; set; } }
public sealed class ImportLanguageForm { [Required, StringLength(1_000_000)] public string Json { get; set; } = ""; }
