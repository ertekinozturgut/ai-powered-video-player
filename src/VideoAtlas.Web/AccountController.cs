using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

[Route("account"), RequestSizeLimit(16384)]
public sealed class AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
    IOptions<AccountOptions> options, ILogger<AccountController> logger) : Controller
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
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return Redirect("/account/login"); }

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
        return RedirectToAction(nameof(Password));
    }
}
