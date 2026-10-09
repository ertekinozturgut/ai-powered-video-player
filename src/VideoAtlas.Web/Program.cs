using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoAtlas.Infrastructure;
using VideoAtlas.Web;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 540_000_000; o.AddServerHeader = false; });
builder.Services.Configure<FormOptions>(o => { o.MultipartBodyLengthLimit = 538_000_000; o.ValueLengthLimit = 2_100_000; });
builder.Services.AddVideoAtlas(builder.Configuration);
builder.Services.AddAuthorization(o => o.AddPolicy("Administrator", policy =>
    policy.RequireAuthenticatedUser().AddRequirements(new AdministratorRequirement())));
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AdministratorHandler>();
builder.Services.AddControllersWithViews(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.SuppressXFrameOptionsHeader = true;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHttpsRedirection(o => o.HttpsPort = 443);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    var knownProxy = builder.Configuration.GetValue<string>("ReverseProxy:KnownAddress");
    if (!string.IsNullOrWhiteSpace(knownProxy)) o.KnownProxies.Add(System.Net.IPAddress.Parse(knownProxy));
});
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "VideoAtlas.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.LoginPath = "/account/login";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    o.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
var dataRoot = Path.GetFullPath(builder.Configuration.GetValue<string>("Storage:Root") ?? "data");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataRoot, "keys"))).SetApplicationName("VideoAtlas");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddConcurrencyLimiter("upload", options => { options.PermitLimit = 2; options.QueueLimit = 0; });
    o.AddPolicy("review", context => RateLimitPartition.GetFixedWindowLimiter(context.User.Identity?.Name ?? "anonymous", _ => new() { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter("server", _ => new() { PermitLimit = 80, QueueLimit = 0 }));
});

var app = builder.Build();
Directory.CreateDirectory(dataRoot);
var accountCommand = args.Any(a => a is "--create-user" or "--reset-password" or "--grant-admin" or "--revoke-admin");
var providerCommand = args.Contains("--configure-providers", StringComparer.Ordinal);
// SQLite worker recovery assumes exactly one application process per data volume.
using var instanceLock = accountCommand || providerCommand ? null : new FileStream(Path.Combine(dataRoot, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await scope.ServiceProvider.GetRequiredService<ProviderSettingsStore>().InitializeAsync(
        scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value, CancellationToken.None);
    if (providerCommand)
    {
        await ProviderProvisioning.RunAsync(scope.ServiceProvider);
        return;
    }
    if (accountCommand)
    {
        await AccountProvisioning.RunAsync(scope.ServiceProvider, args);
        return;
    }
}
app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeaders>();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503)).AllowAnonymous();
await app.RunAsync();
public partial class Program;
