using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed partial class ProductionTests
{
    [GeneratedRegex("name=\"csrf-token\" content=\"([^\"]+)\"", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Csrf();
    [Fact] public async Task ProductionRedirectsHttpClosesRegistrationAndIssuesSecureSessionCookies()
    {
        await using var app = new ProductionAppFactory();
        using var insecure = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.TemporaryRedirect, (await insecure.GetAsync("/account/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await insecure.GetAsync("/health/ready")).StatusCode);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/account/register")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var result = await users.CreateAsync(new IdentityUser { UserName = "production@example.invalid", Email = "production@example.invalid" }, "Synthetic-Test!234");
            Assert.True(result.Succeeded);
        }
        var login = await client.GetAsync("/account/login");
        var html = await login.Content.ReadAsStringAsync(); var token = Csrf().Match(html).Groups[1].Value;
        Assert.NotEmpty(token);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.Contains("secure", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Email"] = "production@example.invalid", ["Password"] = "Synthetic-Test!234", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("VideoAtlas.Session=", StringComparison.Ordinal) && c.Contains("secure", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/videos")).StatusCode);
    }
}
internal sealed class ProductionAppFactory : VideoAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Production");
        builder.ConfigureTestServices(services => services.PostConfigure<AccountOptions>(o => o.AllowRegistration = false));
    }
}
