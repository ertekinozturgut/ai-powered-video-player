using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoAtlas.Application;

namespace VideoAtlas.Web;

[Authorize(Policy = "Administrator"), Route("admin/providers"), RequestSizeLimit(16384)]
public sealed class AdminController(IProviderAdministration providers) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await providers.ReadAsync(ct));
    [HttpPost("select")]
    public async Task<IActionResult> Select(ProviderSelectionForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await providers.SelectAsync(new(form.Speech, form.Chat, form.Embedding, form.Version, form.SecondarySpeech), User.Identity!.Name!, ct);
                TempData["ProviderMessage"] = "Aktif sağlayıcılar kaydedildi. Yeni işler/istekler yeni seçimi kullanır; devam eden iş aynı sağlayıcılarla tamamlanır.";
                return RedirectToAction(nameof(Index));
            }
            catch (AppException ex) { ModelState.AddModelError("", ex.Message); Response.StatusCode = ex.Status; }
        }
        return View("Index", await providers.ReadAsync(ct));
    }
    [HttpGet("{id}")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var profile = (await providers.ReadAsync(ct)).Profiles.SingleOrDefault(p => p.Id == id);
        if (profile is null) return NotFound();
        ViewBag.Profile = profile;
        return View(new ProviderEditForm { Endpoint = profile.Endpoint, Model = profile.Model, Dimensions = profile.Dimensions, Version = profile.Version });
    }
    [HttpPost("{id}")]
    public async Task<IActionResult> Edit(string id, ProviderEditForm form, CancellationToken ct)
    {
        var secret = form.NewSecret;
        // Even validation errors must not echo a credential into HTML/ModelState.
        form.NewSecret = null; ModelState.Remove(nameof(form.NewSecret));
        if (ModelState.IsValid)
        {
            try
            {
                await providers.SaveAsync(new(id, form.Endpoint, form.Model, form.Dimensions, secret, form.RemoveSecret, form.Version), User.Identity!.Name!, ct);
                TempData["ProviderMessage"] = "Sağlayıcı ayarları kaydedildi. Kaydetmek canlı bağlantının doğrulandığı anlamına gelmez.";
                return RedirectToAction(nameof(Index));
            }
            catch (AppException ex) { ModelState.AddModelError("", ex.Message); Response.StatusCode = ex.Status; }
        }
        ViewBag.Profile = (await providers.ReadAsync(ct)).Profiles.SingleOrDefault(p => p.Id == id);
        if (ViewBag.Profile is null) return NotFound();
        return View(form);
    }
}
public sealed class ProviderSelectionForm
{
    [StringLength(80)] public string? SecondarySpeech { get; set; }
    [StringLength(80)] public string? Speech { get; set; }
    [StringLength(80)] public string? Chat { get; set; }
    [StringLength(80)] public string? Embedding { get; set; }
    [Range(0, int.MaxValue)] public int Version { get; set; }
}
public sealed class ProviderEditForm
{
    [Required, StringLength(1000)] public string Endpoint { get; set; } = "";
    [Required, StringLength(200)] public string Model { get; set; } = "";
    [Range(128, 3072)] public int Dimensions { get; set; } = 512;
    [DataType(DataType.Password)] public string? NewSecret { get; set; }
    public bool RemoveSecret { get; set; }
    [Range(0, int.MaxValue)] public int Version { get; set; }
}
