using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>Registers accounts and the three flows into an <see cref="IServiceCollection"/>.</summary>
public static class RaskAuthServiceCollectionExtensions
{
    /// <summary>
    /// Adds ASP.NET Core Identity over the application's own context, the cookie scheme, and the
    /// host-neutral <see cref="IAuth"/> the pages and endpoints are written against.
    /// </summary>
    /// <typeparam name="TContext">The application context that owns the account tables.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options.</param>
    /// <remarks>
    /// Map the tables with <c>modelBuilder.AddRaskAuth()</c> in <c>OnModelCreating</c>, then create the
    /// schema with <c>rask db add AddAuth &amp;&amp; rask db update</c>.
    /// </remarks>
    public static IServiceCollection AddRaskAuth<TContext>(
        this IServiceCollection services, Action<AuthOptions>? configure = null)
        where TContext : DbContext =>
        services.AddRaskAuth<TContext, RaskUser>(configure);

    /// <summary>
    /// Adds accounts for an application-supplied user type deriving from <see cref="RaskUser"/>.
    /// </summary>
    /// <typeparam name="TContext">The application context that owns the account tables.</typeparam>
    /// <typeparam name="TUser">The application's user entity.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options.</param>
    /// <remarks>
    /// The options go in with <c>TryAddSingleton</c>, so in an app that calls this twice the
    /// <b>first</b> call wins and the second one's configuration is discarded — the same shape, and the
    /// same hazard, as <c>AddRask</c> (RASK056). Configure it once.
    /// </remarks>
    public static IServiceCollection AddRaskAuth<TContext, TUser>(
        this IServiceCollection services, Action<AuthOptions>? configure = null)
        where TContext : DbContext
        where TUser : RaskUser, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AuthOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FirstRunToken>();
        services.TryAddSingleton<IInstanceClaimStore, InstanceClaimStore<TContext>>();

        // Identity's EF stores resolve the context from DI as a scoped service, but a Rask app
        // registers an IDbContextFactory (every battery creates its own short-lived context). Bridge
        // the two rather than making the app register the context twice — TryAdd, so an app that does
        // register one keeps it.
        services.TryAddScoped(sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());

        // SignInManager takes an IHttpContextAccessor even for the checks that never touch a request.
        services.AddHttpContextAccessor();

        services
            .AddIdentityCore<TUser>(o =>
            {
                o.User.RequireUniqueEmail = true;

                o.Password.RequiredLength = options.MinimumPasswordLength;
                o.Password.RequireDigit = options.RequireMixedCasePasswords;
                o.Password.RequireLowercase = options.RequireMixedCasePasswords;
                o.Password.RequireUppercase = options.RequireMixedCasePasswords;
                // Length is what resists guessing; demanding punctuation mostly produces "Password1!".
                o.Password.RequireNonAlphanumeric = false;

                o.Lockout.MaxFailedAccessAttempts = options.MaxFailedAccessAttempts;
                o.Lockout.DefaultLockoutTimeSpan = options.LockoutDuration;
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<TContext>()
            .AddSignInManager()
            // Registered now although verification and reset are not shipped yet: the providers are
            // what those flows are built from, and adding them later would change the token format for
            // apps that had already stored one.
            .AddDefaultTokenProviders();

        services.TryAddSingleton<IRoleSeedContexts, RoleSeedContexts<TContext>>();
        services.AddScoped<AccountService<TUser>>();

        // The endpoints resolve the store without naming the user type — MapRaskAuth() is a
        // parameterless extension method, so it has no way to know which one this app configured.
        services.TryAddScoped<IAccounts>(sp => sp.GetRequiredService<AccountService<TUser>>());
        services.TryAddScoped<IAuth, ServerAuth<TUser>>();

        services.AddHostedService<FirstRunTokenInitializer>();

        // Only when the app has not already set up authentication itself. Registering a scheme twice is
        // not a no-op — AuthenticationOptions.AddScheme throws "Scheme already exists" — so a battery
        // that always registered one would break at STARTUP for two ordinary cases: an app that brings
        // its own OIDC or JWT scheme (the pattern docs/authentication-providers.md documents), and any
        // app still carrying a hand-written AddAuthentication().AddCookie() from before this battery.
        //
        // IAuthenticationSchemeProvider is the marker AddAuthentication leaves behind, and it is the same
        // one RaskApp reads to decide whether to call UseAuthentication.
        if (!services.Any(d => d.ServiceType == typeof(IAuthenticationSchemeProvider)))
        {
            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o =>
                {
                    o.Cookie.Name = options.CookieName;
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    // Lax, not Strict: Strict withholds the cookie on the first navigation that arrives
                    // from another site, so a visitor following a link into a protected page would land
                    // signed-out and be bounced to /login despite having a valid session.
                    o.Cookie.SameSite = SameSiteMode.Lax;
                    o.LoginPath = options.LoginPath;
                    o.LogoutPath = options.LogoutPath;
                    o.AccessDeniedPath = options.AccessDeniedPath;
                    o.ExpireTimeSpan = options.ExpireTimeSpan;
                    o.SlidingExpiration = options.SlidingExpiration;
                });
        }

        // AddRask() also calls this; it is idempotent, and Rask.Auth must not depend on being wired
        // after the host.
        services.AddAuthorization();

        return services;
    }
}
