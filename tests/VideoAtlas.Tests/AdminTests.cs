using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Application;
using Xunit;

namespace VideoAtlas.Tests;

public sealed partial class AdminTests
{
    [GeneratedRegex("name=\"csrf-token\" content=\"([^\"]+)\"", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Csrf();
    [Fact] public async Task AnonymousAndRegularUsersCannotUseAdministration()
    {
        await using var app = new VideoAppFactory();
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/admin/providers")).StatusCode);
        using var regular = await LoginAsync(app, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await regular.GetAsync("/admin/providers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await regular.PostAsync("/admin/providers/select", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }
    [Fact] public async Task AdminFormsRequireCsrfAndNeverEchoSavedOrRejectedSecrets()
    {
        await using var app = new VideoAppFactory();
        using var client = await LoginAsync(app, true);
        var html = await client.GetStringAsync("/admin/providers/chat-openrouter");
        var token = Csrf().Match(html).Groups[1].Value;
        var data = new Dictionary<string,string>
        {
            ["Endpoint"]="https://openrouter.ai/api/v1/",["Model"]="openai/gpt-4.1-mini",["Dimensions"]="512",["Version"]="0",["NewSecret"]="synthetic-private-admin-key"
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/admin/providers/chat-openrouter",new FormUrlEncodedContent(data))).StatusCode);
        data["__RequestVerificationToken"]=token;
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/admin/providers/chat-openrouter",new FormUrlEncodedContent(data))).StatusCode);
        Assert.DoesNotContain(data["NewSecret"],await client.GetStringAsync("/admin/providers/chat-openrouter"));
        data["Endpoint"]="https://attacker.example.invalid/";data["Version"]="1";
        var rejected = await client.PostAsync("/admin/providers/chat-openrouter",new FormUrlEncodedContent(data));
        Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        Assert.DoesNotContain(data["NewSecret"], await rejected.Content.ReadAsStringAsync());
        Assert.Contains("value=\"\"", await rejected.Content.ReadAsStringAsync());
        var dashboard = await client.GetStringAsync("/admin/providers");
        Assert.Contains("Anahtar kayıtlı", dashboard);
        Assert.DoesNotContain(data["NewSecret"], dashboard);
    }
    [Fact] public async Task RevokedAdministratorLosesAccessEvenWithExistingCookie()
    {
        await using var app = new VideoAppFactory();
        using var client = await LoginAsync(app, true);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/providers")).StatusCode);
        using (var scope=app.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user=await users.FindByEmailAsync("admin@example.invalid");
            Assert.True((await users.RemoveFromRoleAsync(user!,"Administrator")).Succeeded);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/providers")).StatusCode);
    }
    [Fact] public async Task ActiveCredentialCannotBeRemovedAndSelectionIsPersistedAcrossScopes()
    {
        await using var app=new VideoAppFactory();using var client=app.CreateClient();
        using(var scope=app.Services.CreateScope())
        {
            var admin=scope.ServiceProvider.GetRequiredService<IProviderAdministration>();
            var state=await admin.ReadAsync(default);var p=state.Profiles.Single(p=>p.Id=="chat-openrouter");
            await admin.SaveAsync(new(p.Id,p.Endpoint,p.Model,p.Dimensions,"synthetic-secret",false,p.Version),"admin",default);
            state=await admin.ReadAsync(default);
            await admin.SelectAsync(new(null,p.Id,null,state.Selection.Version),"admin",default);
        }
        using(var scope=app.Services.CreateScope())
        {
            var admin=scope.ServiceProvider.GetRequiredService<IProviderAdministration>();
            var state=await admin.ReadAsync(default);Assert.Equal("chat-openrouter",state.Selection.Chat);
            var p=state.Profiles.Single(p=>p.Id==state.Selection.Chat);
            var error=await Assert.ThrowsAsync<AppException>(()=>admin.SaveAsync(new(p.Id,p.Endpoint,p.Model,p.Dimensions,null,true,p.Version),"admin",default));
            Assert.Equal("provider_active",error.Code);
        }
    }
    private static async Task<HttpClient> LoginAsync(VideoAppFactory app, bool admin)
    {
        var email=admin?"admin@example.invalid":"regular@example.invalid";
        using(var scope=app.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user=new IdentityUser{UserName=email,Email=email};
            Assert.True((await users.CreateAsync(user,"Synthetic-Test!234")).Succeeded);
            if(admin)
            {
                var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                Assert.True((await roles.CreateAsync(new IdentityRole("Administrator"))).Succeeded);
                Assert.True((await users.AddToRoleAsync(user,"Administrator")).Succeeded);
            }
        }
        var client=app.CreateClient(new(){AllowAutoRedirect=false});
        var token=Csrf().Match(await client.GetStringAsync("/account/login")).Groups[1].Value;
        var result=await client.PostAsync("/account/login",new FormUrlEncodedContent(new Dictionary<string,string>
        {["Email"]=email,["Password"]="Synthetic-Test!234",["__RequestVerificationToken"]=token}));
        Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);return client;
    }
}
