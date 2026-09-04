using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Auth;

namespace Rask.Example.Auth.WasmCookie.Host;

/// <summary>
/// The application context. It carries nothing of its own — the account tables are all this sample
/// stores — but the accounts battery needs a context to put them in.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.AddRaskAuth();
    }
}

/// <summary>Creates the schema and the two accounts this sample signs in with.</summary>
/// <remarks>
/// A real app seeds nobody: the first person to register becomes the administrator, and while no account
/// exists that registration wants the one-time token from the startup log. That is right for something
/// you deploy and wrong for a sample you clone and run — and it is what the browser journey drives, so
/// it has to be deterministic.
/// </remarks>
public static class AuthSeed
{
    /// <summary>The demo administrator.</summary>
    public const string AdminEmail = "ada@example.com";

    /// <summary>A demo account with no admin role, so role gating has something to gate.</summary>
    public const string UserEmail = "bob@example.com";

    /// <summary>The password both demo accounts use.</summary>
    public const string Password = "Password1";

    /// <summary>Creates the database and the demo account if they are not there yet.</summary>
    /// <param name="services">The application's service provider.</param>
    public static async Task EnsureDemoUserAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        await using (var db = await factory.CreateDbContextAsync().ConfigureAwait(false))
        {
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        }

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in RaskRoles.All)
        {
            if (!await roles.RoleExistsAsync(role).ConfigureAwait(false))
            {
                await roles.CreateAsync(new IdentityRole(role)).ConfigureAwait(false);
            }
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<RaskUser>>();

        await EnsureAsync(users, AdminEmail, RaskRoles.Admin).ConfigureAwait(false);
        await EnsureAsync(users, UserEmail, RaskRoles.User).ConfigureAwait(false);
    }

    private static async Task EnsureAsync(UserManager<RaskUser> users, string email, string role)
    {
        if (await users.FindByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            return;
        }

        var user = new RaskUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedUtc = DateTime.UtcNow,
        };

        if ((await users.CreateAsync(user, Password).ConfigureAwait(false)).Succeeded)
        {
            await users.AddToRoleAsync(user, role).ConfigureAwait(false);
        }
    }
}
