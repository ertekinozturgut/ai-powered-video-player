using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class ExternalApiTests : IClassFixture<VideoAppFactory>
{
    private readonly VideoAppFactory factory;
    public ExternalApiTests(VideoAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task CredentialAuthenticatesOwnerScopedApiAndWritesAuditLog()
    {
        string secret; Guid credentialId; string ownerId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var email = $"api-{Guid.NewGuid():N}@example.invalid";
            var user = new IdentityUser { UserName = email, Email = email };
            Assert.True((await users.CreateAsync(user, "Synthetic-Test!234")).Succeeded);
            ownerId = user.Id;
            var created = await scope.ServiceProvider.GetRequiredService<ApiCredentialService>().CreateAsync(ownerId, "CI integration",
                [ExternalApiScopes.Read, ExternalApiScopes.Write], 30, CancellationToken.None);
            secret = created.Secret; credentialId = created.Credential.Id;
            Assert.DoesNotContain(secret, created.Credential.SecretHash);
        }

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent([0, 1, 2, 3]), "file", "sample.mp3");
        upload.Add(new StringContent("API sample"), "title");
        upload.Add(new StringContent("true"), "consent");
        var createdResponse = await client.PostAsync("/api/v1/analyses", upload);
        Assert.Equal(HttpStatusCode.Accepted, createdResponse.StatusCode);

        var response = await client.GetAsync("/api/v1/analyses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiList>();
        Assert.NotNull(body); Assert.Single(body.Items);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(db.ApiRequestLogs, x => x.OwnerId == ownerId && x.CredentialId == credentialId && x.StatusCode == 200);
        Assert.Contains(db.Videos, x => x.OwnerId == ownerId && x.CreatedVia == "api" && x.ApiCredentialId == credentialId);
    }

    [Fact]
    public async Task InvalidCredentialIsRejectedAndDeleteRequiresScope()
    {
        using var invalid = factory.CreateClient(new() { AllowAutoRedirect = false });
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "vat_0000000000000000.invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.GetAsync("/api/v1/analyses")).StatusCode);

        string secret;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var email = $"readonly-{Guid.NewGuid():N}@example.invalid";
            var user = new IdentityUser { UserName = email, Email = email };
            Assert.True((await users.CreateAsync(user, "Synthetic-Test!234")).Succeeded);
            secret = (await scope.ServiceProvider.GetRequiredService<ApiCredentialService>().CreateAsync(user.Id, "Read only",
                [ExternalApiScopes.Read], 30, CancellationToken.None)).Secret;
        }
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/v1/analyses/{Guid.NewGuid()}")).StatusCode);
    }

    private sealed record ApiList(IReadOnlyList<object> Items, int Page, int PageSize, int Total);
}
