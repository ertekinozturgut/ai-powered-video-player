using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace VideoAtlas.Tests;

public sealed partial class LocalizationTests : IClassFixture<VideoAppFactory>
{
    private readonly VideoAppFactory factory;
    public LocalizationTests(VideoAppFactory factory) => this.factory = factory;

    [GeneratedRegex("name=\"csrf-token\" content=\"([^\"]+)\"", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Csrf();

    [Fact]
    public async Task SelectingInterfaceLanguagePersistsAndReturnsFreshCatalog()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var registerToken = await TokenAsync(client, "/account/register");
        var register = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = $"language-{Guid.NewGuid():N}@example.invalid",
            ["Password"] = "Synthetic-Test!234",
            ["__RequestVerificationToken"] = registerToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);

        var token = await TokenAsync(client, "/");
        var select = await client.PostAsync("/language/select", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = "en",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, select.StatusCode);
        Assert.Contains(select.Headers.GetValues("Set-Cookie"), value => value.StartsWith("VideoAtlas.Language=en", StringComparison.Ordinal));

        var catalog = await client.GetFromJsonAsync<ClientCatalog>("/api/localization");
        Assert.NotNull(catalog);
        Assert.Equal("en", catalog.Language);
        Assert.Contains("en", catalog.Supported);
        Assert.Equal("Video library", catalog.Texts["Video kütüphanesi"]);
        Assert.Contains("no-store", (await client.GetAsync("/api/localization")).Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AnonymousBrowserPreferenceSetsCookieAndSelectsEnglish()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var token = await TokenAsync(client, "/account/login");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/language/preference")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = "en" })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("VideoAtlas.Language=en", StringComparison.Ordinal));
        var catalog = await client.GetFromJsonAsync<ClientCatalog>("/api/localization");
        Assert.Equal("en", catalog!.Language);
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var token = Csrf().Match(html).Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private sealed record ClientCatalog(string Language, string[] Supported, Dictionary<string, string> Texts);
}
