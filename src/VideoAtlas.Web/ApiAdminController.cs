using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

public sealed record ApiAdminCredentialView(Guid Id, string OwnerEmail, string Name, string Prefix, string Scopes,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);
public sealed record ApiAdminUserOption(string Id, string Email);
public sealed record ApiAdminView(IReadOnlyList<ApiAdminCredentialView> Credentials, IReadOnlyList<ApiRequestLogView> Logs,
    IReadOnlyList<ApiAdminUserOption> Users, int ActiveCredentials, int RequestsToday, int FailedToday, string? CreatedSecret);
public sealed class ApiAdminCreateCredentialForm
{
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "Integration";
    public int ExpiresInDays { get; set; } = 90;
    public bool Read { get; set; } = true;
    public bool Write { get; set; } = true;
    public bool Delete { get; set; }
}

[Authorize(Policy = "Administrator"), Route("admin/api")]
public sealed class ApiAdminController(AppDbContext db, TimeProvider clock, ApiCredentialService credentialService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = db.Set<IdentityUser>();
        var credentials = await db.ApiCredentials.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200)
            .Join(users, x => x.OwnerId, u => u.Id, (x, u) => new ApiAdminCredentialView(x.Id, u.Email ?? u.UserName ?? "—",
                x.Name, x.Prefix, x.Scopes, x.CreatedAt, x.ExpiresAt, x.LastUsedAt, x.RevokedAt)).ToListAsync(ct);
        var logs = await db.ApiRequestLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(500)
            .Select(x => new ApiRequestLogView(x.Id, x.CreatedAt, x.CredentialPrefix ?? "—", x.Method, x.Path,
                x.StatusCode, x.DurationMs, x.VideoId, x.TraceId)).ToListAsync(ct);
        var day = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var active = await db.ApiCredentials.CountAsync(x => x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > clock.GetUtcNow()), ct);
        var today = await db.ApiRequestLogs.CountAsync(x => x.CreatedAt >= day, ct);
        var failed = await db.ApiRequestLogs.CountAsync(x => x.CreatedAt >= day && x.StatusCode >= 400, ct);
        var userOptions = await users.AsNoTracking().OrderBy(x => x.Email).Select(x => new ApiAdminUserOption(x.Id, x.Email ?? x.UserName ?? "—")).ToListAsync(ct);
        return View(new ApiAdminView(credentials, logs, userOptions, active, today, failed, TempData["CreatedApiSecret"] as string));
    }

    [HttpPost("credentials")]
    public async Task<IActionResult> CreateCredential(ApiAdminCreateCredentialForm form, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == form.OwnerId, ct)) return NotFound();
        var scopes = new List<string>();
        if (form.Read) scopes.Add(ExternalApiScopes.Read);
        if (form.Write) scopes.Add(ExternalApiScopes.Write);
        if (form.Delete) scopes.Add(ExternalApiScopes.Delete);
        try
        {
            var created = await credentialService.CreateAsync(form.OwnerId, form.Name, scopes, form.ExpiresInDays, ct);
            TempData["CreatedApiSecret"] = created.Secret;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { TempData["ApiAdminMessage"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("credentials/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeCredential(Guid id, CancellationToken ct)
    {
        var credential = await db.ApiCredentials.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (credential is null) return NotFound();
        credential.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }
}
