using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using VideoAtlas.Application;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

[Route("account"), RequestSizeLimit(16384)]
public sealed class AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
    IOptions<AccountOptions> options, ILogger<AccountController> logger, ILocalizationAdministration catalog, AppDbContext db,
    TimeProvider clock, ApiCredentialService apiCredentials) : Controller
{
    // Login and opt-in registration are entry points for unauthenticated users.
    [AllowAnonymous, HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) { ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null; ViewBag.AllowRegistration = options.Value.AllowRegistration; return View(new AccountForm()); }
    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(AccountForm form, string? returnUrl = null)
    {
        ViewBag.AllowRegistration = options.Value.AllowRegistration;
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        if (!ModelState.IsValid) return View(form);
        var result = await signIn.PasswordSignInAsync(form.Email, form.Password, false, true);
        if (result.Succeeded) return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        logger.LogWarning("Authentication failed: {TraceId}", HttpContext.TraceIdentifier);
        ModelState.AddModelError("", "E-posta veya parola hatalı. Çok fazla denemede hesabınız geçici olarak kilitlenir.");
        return View(form);
    }
    [AllowAnonymous, HttpGet("register")]
    public IActionResult Register() => options.Value.AllowRegistration ? View(new AccountForm()) : NotFound();
    [AllowAnonymous, HttpPost("register"), EnableRateLimiting("login")]
    public async Task<IActionResult> Register(AccountForm form)
    {
        if (!options.Value.AllowRegistration) return NotFound();
        if (!ModelState.IsValid) return View(form);
        var user = new IdentityUser { UserName = form.Email, Email = form.Email };
        var result = await users.CreateAsync(user, form.Password);
        if (result.Succeeded) { await signIn.SignInAsync(user, false); return Redirect("/"); }
        ModelState.AddModelError("", "Hesap açılamadı. Başka bir e-posta deneyin; parola en az 12 karakter, büyük/küçük harf, rakam ve sembol içermelidir.");
        return View(form);
    }
    [AllowAnonymous, HttpGet("forgot-password")]
    public IActionResult ForgotPassword() => View(new PasswordRecoveryRequestForm());

    [AllowAnonymous, HttpPost("forgot-password"), EnableRateLimiting("login")]
    public IActionResult ForgotPassword(PasswordRecoveryRequestForm form)
    {
        // Never reveal whether an account exists. This local installation has no mail transport;
        // password reset remains an administrator-mediated operation.
        if (!ModelState.IsValid) return View(form);
        logger.LogInformation("Password recovery requested: {TraceId}", HttpContext.TraceIdentifier);
        TempData["RecoveryMessage"] = "Hesap mevcutsa parola sıfırlama talebi kaydedildi. Yerel kurulumlarda çalışma alanı yöneticiniz yeni parolayı güvenli kanaldan paylaşır.";
        return RedirectToAction(nameof(ForgotPassword));
    }
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return Redirect("/account/login"); }

    [Authorize, HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException();
        var preference = await db.UserLanguagePreferences.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        var language = await catalog.ResolveLanguageAsync(user.Id, null, ct);
        return View(await BuildAccountPageAsync(user, new AccountProfileForm { Email = user.Email ?? user.UserName ?? "", DisplayName = preference?.DisplayName ?? "", Language = language }, ct));
    }
    [Authorize, HttpPost("profile")]
    public async Task<IActionResult> Profile([Bind(Prefix = "Profile")] AccountProfileForm form, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException();
        if (!ModelState.IsValid) return View(await BuildAccountPageAsync(user, form, ct));
        var emailChanged = !string.Equals(user.Email, form.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        if (emailChanged && (string.IsNullOrWhiteSpace(form.CurrentPassword) || !await users.CheckPasswordAsync(user, form.CurrentPassword)))
            ModelState.AddModelError(nameof(form.CurrentPassword), "E-posta değişikliği için mevcut parolanızı doğrulayın.");
        if (!ModelState.IsValid) return View(await BuildAccountPageAsync(user, form, ct));
        if (emailChanged)
        {
            var email = form.Email.Trim(); var change = await users.SetEmailAsync(user, email);
            if (change.Succeeded) change = await users.SetUserNameAsync(user, email);
            if (!change.Succeeded) { foreach (var error in change.Errors) ModelState.AddModelError("", error.Description); return View(await BuildAccountPageAsync(user, form, ct)); }
        }
        var preference = await db.UserLanguagePreferences.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (preference is null) { preference = new UserLanguagePreferenceEntity { UserId = user.Id }; db.UserLanguagePreferences.Add(preference); }
        preference.DisplayName = form.DisplayName.Trim(); preference.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct);
        if (!await catalog.SetUserLanguageAsync(user.Id, form.Language, ct))
        {
            ModelState.AddModelError(nameof(form.Language), "Seçilen arayüz dili kullanıma açık değil.");
            return View(await BuildAccountPageAsync(user, form, ct));
        }
        Response.Cookies.Append("VideoAtlas.Language", form.Language, new CookieOptions { IsEssential = true, HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, Expires = clock.GetUtcNow().AddYears(1), Path = "/" });
        await signIn.RefreshSignInAsync(user);
        TempData["AccountMessage"] = "Hesap bilgileriniz kaydedildi."; return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost("api-credentials")]
    public async Task<IActionResult> CreateApiCredential(ApiCredentialCreateForm form, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException();
        if (!ModelState.IsValid) { TempData["AccountMessage"] = "API bilgisi oluşturulamadı; alanları kontrol edin."; return RedirectToAction(nameof(Profile)); }
        var scopes = new List<string>();
        if (form.Read) scopes.Add(ExternalApiScopes.Read);
        if (form.Write) scopes.Add(ExternalApiScopes.Write);
        if (form.Delete) scopes.Add(ExternalApiScopes.Delete);
        try
        {
            var created = await apiCredentials.CreateAsync(user.Id, form.Name, scopes, form.ExpiresInDays, ct);
            TempData["CreatedApiSecret"] = created.Secret;
            TempData["AccountMessage"] = "API kimlik bilgisi oluşturuldu. Gizli anahtarı şimdi kopyalayın; tekrar gösterilmeyecek.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { TempData["AccountMessage"] = ex.Message; }
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost("api-credentials/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeApiCredential(Guid id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException();
        var credential = await db.ApiCredentials.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        if (credential is null) return NotFound();
        credential.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        TempData["AccountMessage"] = "API kimlik bilgisi iptal edildi.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpGet("password")]
    public IActionResult Password() => View(new PasswordResetForm { Email = User.Identity?.Name ?? "" });

    [Authorize, HttpPost("password")]
    public async Task<IActionResult> Password(PasswordResetForm form)
    {
        if (!string.Equals(form.Email, User.Identity?.Name, StringComparison.OrdinalIgnoreCase)) ModelState.AddModelError(nameof(form.Email), "Yalnızca kendi hesabınızın parolasını değiştirebilirsiniz.");
        if (!ModelState.IsValid) return View(form);
        var user = await users.FindByEmailAsync(form.Email);
        if (user is null) return Forbid();
        var result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), form.NewPassword);
        if (!result.Succeeded) { foreach (var error in result.Errors) ModelState.AddModelError("", error.Description); return View(form); }
        await signIn.RefreshSignInAsync(user);
        TempData["AccountMessage"] = "Parolanız değiştirildi.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<AccountPageView> BuildAccountPageAsync(IdentityUser user, AccountProfileForm form, CancellationToken ct)
    {
        var credentials = await db.ApiCredentials.AsNoTracking().Where(x => x.OwnerId == user.Id).OrderByDescending(x => x.CreatedAt).Take(25)
            .Select(x => new ApiCredentialView(x.Id, x.Name, x.Prefix, x.Scopes, x.CreatedAt, x.ExpiresAt, x.LastUsedAt, x.RevokedAt != null)).ToListAsync(ct);
        var analyses = await db.Videos.AsNoTracking().Where(x => x.OwnerId == user.Id && x.CreatedVia == "api").OrderByDescending(x => x.CreatedAt).Take(25)
            .Select(x => new ApiAnalysisView(x.Id, x.Title, x.Status, x.Stage, x.Progress, x.CreatedAt)).ToListAsync(ct);
        var logs = await db.ApiRequestLogs.AsNoTracking().Where(x => x.OwnerId == user.Id).OrderByDescending(x => x.CreatedAt).Take(50)
            .Select(x => new ApiRequestLogView(x.Id, x.CreatedAt, x.CredentialPrefix ?? "—", x.Method, x.Path, x.StatusCode,
                x.DurationMs, x.VideoId, x.TraceId)).ToListAsync(ct);
        return new(form, await catalog.EnabledAsync(ct), credentials, analyses, logs, TempData["CreatedApiSecret"] as string);
    }
}
