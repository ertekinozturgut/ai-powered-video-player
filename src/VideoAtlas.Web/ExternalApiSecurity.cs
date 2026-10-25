using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

public static class ApiKeyDefaults
{
    public const string Scheme = "VideoAtlasApiKey";
    public const string CredentialIdClaim = "videoatlas:credential_id";
    public const string CredentialPrefixClaim = "videoatlas:credential_prefix";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ApiCredentialService credentials) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header)) return AuthenticateResult.NoResult();
        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Bearer API credential is required.");
        var token = value[7..].Trim();
        var credential = await credentials.ValidateAsync(token, Context.RequestAborted);
        if (credential is null) return AuthenticateResult.Fail("API credential is invalid, expired, or revoked.");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, credential.OwnerId),
            new(ApiKeyDefaults.CredentialIdClaim, credential.Id.ToString()),
            new(ApiKeyDefaults.CredentialPrefixClaim, credential.Prefix),
            new(ClaimTypes.Name, credential.Name)
        };
        claims.AddRange(credential.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(scope => new Claim("scope", scope)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyDefaults.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyDefaults.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        return Response.WriteAsJsonAsync(new { title = "A valid API credential is required.", status = 401,
            code = "invalid_api_credential", traceId = Context.TraceIdentifier });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/problem+json";
        return Response.WriteAsJsonAsync(new { title = "The API credential does not include the required scope.", status = 403,
            code = "insufficient_scope", traceId = Context.TraceIdentifier });
    }
}

public sealed class ExternalApiAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopes, TimeProvider clock)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1")) { await next(context); return; }
        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;
        try { await next(context); }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var credentialText = context.User.FindFirstValue(ApiKeyDefaults.CredentialIdClaim);
                Guid? credentialId = Guid.TryParse(credentialText, out var parsed) ? parsed : null;
                string? prefix = context.User.FindFirstValue(ApiKeyDefaults.CredentialPrefixClaim);
                if (prefix is null && context.Request.Headers.TryGetValue("Authorization", out var authorization))
                    ApiCredentialService.TryPrefix(authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim(), out prefix!);
                var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                db.ApiRequestLogs.Add(new ApiRequestLogEntity
                {
                    CredentialId = credentialId, OwnerId = owner, CredentialPrefix = prefix,
                    Method = context.Request.Method, Path = context.Request.Path.Value?.Truncate(500) ?? "",
                    StatusCode = failure is AppException appFailure ? appFailure.Status : failure is null ? context.Response.StatusCode : 500,
                    DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    RequestBytes = context.Request.ContentLength, ResponseBytes = context.Response.ContentLength,
                    RemoteAddressHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)))[..16],
                    TraceId = context.TraceIdentifier.Truncate(100), ErrorCode = failure is AppException apiFailure ? apiFailure.Code : failure?.GetType().Name.Truncate(100),
                    VideoId = context.Items["ExternalApiVideoId"] as Guid?, CreatedAt = clock.GetUtcNow()
                });
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch { /* Audit persistence must never replace the API response. */ }
        }
    }
}

file static class ExternalApiStringExtensions
{
    public static string Truncate(this string value, int length) => value.Length <= length ? value : value[..length];
}
