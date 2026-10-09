using Microsoft.AspNetCore.Identity;

namespace VideoAtlas.Web;

public static class AccountProvisioning
{
    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        var email = Environment.GetEnvironmentVariable("BOOTSTRAP_EMAIL");
        var password = Environment.GetEnvironmentVariable("BOOTSTRAP_PASSWORD");
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Set BOOTSTRAP_EMAIL securely before running this command.");
        var manager = services.GetRequiredService<UserManager<IdentityUser>>();
        var user = await manager.FindByEmailAsync(email);
        if (args.Any(a => a is "--grant-admin" or "--revoke-admin"))
        {
            if (user is null) throw new InvalidOperationException("Create the account first.");
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync("Administrator")) Ensure(await roles.CreateAsync(new IdentityRole("Administrator")));
            Ensure(args.Contains("--grant-admin", StringComparer.Ordinal)
                ? await manager.AddToRoleAsync(user, "Administrator") : await manager.RemoveFromRoleAsync(user, "Administrator"));
            Ensure(await manager.UpdateSecurityStampAsync(user));
            Console.WriteLine("Administrator role updated. Sign in again.");
            return;
        }
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set BOOTSTRAP_PASSWORD securely before running this command.");
        IdentityResult result;
        if (args.Contains("--reset-password", StringComparer.Ordinal))
        {
            if (user is null) throw new InvalidOperationException("Account does not exist.");
            result = await manager.ResetPasswordAsync(user, await manager.GeneratePasswordResetTokenAsync(user), password);
        }
        else
        {
            if (user is not null) throw new InvalidOperationException("Account already exists; use --reset-password if needed.");
            result = await manager.CreateAsync(new IdentityUser { UserName = email, Email = email, EmailConfirmed = true }, password);
        }
        if (!result.Succeeded) throw new InvalidOperationException("Account operation failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        Console.WriteLine("Account operation completed successfully.");
    }
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Role update failed.");
    }
}
