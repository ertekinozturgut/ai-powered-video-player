using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace VideoAtlas.Web;

public sealed class AdministratorRequirement : IAuthorizationRequirement;
// Check the database on every admin request: revoked roles cannot survive in old cookies.
public sealed class AdministratorHandler(UserManager<IdentityUser> users) : AuthorizationHandler<AdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdministratorRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var user = await users.GetUserAsync(context.User);
        if (user is not null && await users.IsInRoleAsync(user, "Administrator")) context.Succeed(requirement);
    }
}
