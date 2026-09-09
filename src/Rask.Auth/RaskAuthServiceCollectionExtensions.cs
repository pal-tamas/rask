using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
            // What the confirmation and reset links are minted from.
            .AddDefaultTokenProviders();

        // One lifetime, set in one place. The email tells the reader how long the link lasts and the
        // provider decides when it stops working; read from separate settings they drift, and the
        // symptom is a message promising two hours about a token that expired in one.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = options.TokenLifetime);

        services.TryAddScoped<AuthMail>();

        services.TryAddSingleton<IRoleSeedContexts, RoleSeedContexts<TContext>>();
        services.AddScoped<AccountService<TUser>>();

        // The endpoints resolve the store without naming the user type — MapRaskAuth() is a
        // parameterless extension method, so it has no way to know which one this app configured.
        services.TryAddScoped<IAccounts>(sp => sp.GetRequiredService<AccountService<TUser>>());
        services.TryAddScoped<IAuth, ServerAuth<TUser>>();

        // Before the first-run token initializer, so an app whose model never mapped the account tables
        // fails the boot with the line to type rather than at the first registration — Identity's EF
        // stores resolve lazily, so nothing above this notices. See BatteryModelCheck: this reads the
        // MODEL, never the database, so an app that has not run `rask db update` yet still starts.
        services.AddHostedService<AuthModelCheck<TContext, TUser>>();

        services.AddHostedService<FirstRunTokenInitializer>();

        // The cookie scheme is Rask.Auth's, unconditionally: cookies are the only session Rask
        // authenticates, so the battery owns the scheme rather than standing down when the app has
        // wired authentication of its own. An external provider still composes — it adds a CHALLENGE
        // scheme (AddOpenIdConnect, AddGoogle, …) beside this one and signs in through it, which is the
        // ordinary ASP.NET arrangement and the one docs/authentication-providers.md now documents. An
        // app that needs a cookie knob AuthOptions does not carry configures the same named options
        // after AddRaskAuth.
        //
        // AddAuthentication is safe to repeat: it re-registers the core services idempotently and adds
        // one more IConfigureOptions, so calling it here after (or before) the app's own call just
        // leaves the cookie scheme as the default — which is what a Rask app wants either way, since
        // this is the scheme IAuthSignIn drives and the redeem endpoint writes.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);

        // Registering the scheme ITSELF is the one step that is not idempotent: AuthenticationOptions
        // .AddScheme throws "Scheme already exists" — lazily, when the options are first materialised,
        // so the app dies on its first request with a message naming no line to delete. An app carrying
        // a hand-written AddAuthentication().AddCookie() from before this battery is exactly that case,
        // so add the entry only when nothing else already did. The settings below are applied either
        // way: owning the scheme means Rask's configuration wins, not that the app fails to start.
        services.Configure<AuthenticationOptions>(o =>
        {
            if (!o.SchemeMap.ContainsKey(CookieAuthenticationDefaults.AuthenticationScheme))
            {
                o.AddScheme<CookieAuthenticationHandler>(
                    CookieAuthenticationDefaults.AuthenticationScheme, displayName: null);
            }
        });

        // The rest of what AddCookie() wires. Each of these is idempotent on its own, so they run
        // whether or not the scheme entry above was already there.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IPostConfigureOptions<CookieAuthenticationOptions>, PostConfigureCookieAuthenticationOptions>());
        services.TryAddTransient<CookieAuthenticationHandler>();

        // Named options, configured last, so these beat an app's own AddCookie(...) delegate.
        services.Configure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            o =>
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

        // AddRask() also calls this; it is idempotent, and Rask.Auth must not depend on being wired
        // after the host.
        services.AddAuthorization();

        return services;
    }
}
