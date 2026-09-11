using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rask.Hosting.Shared;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>Registers accounts and the three flows into an <see cref="IServiceCollection"/>.</summary>
public static class RaskAuthServiceCollectionExtensions
{
    /// <summary>
    /// Adds ASP.NET Core Identity over the application's own context, the cookie scheme, and the
    /// host-neutral <c>IAuth</c> the pages and endpoints are written against.
    /// </summary>
    /// <typeparam name="TContext">The application context that owns the account tables.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options, after the <c>Rask:Auth</c> configuration section.</param>
    /// <remarks>
    /// Map the tables with <c>modelBuilder.AddRaskAuth()</c> in <c>OnModelCreating</c>, then create the
    /// schema with <c>rask db add AddAuth &amp;&amp; rask db update</c>.
    /// </remarks>
    public static IServiceCollection AddRaskAuth<TContext>(
        this IServiceCollection services, Action<AuthOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // The app's account type, found at compile time by the generator. An app that declares none
        // gets no auth — which is the honest outcome, and the alternative (inventing a user type) is
        // what this change removed.
        return AuthUser.Binding?.Add<TContext>(services, configure) ?? services;
    }

    /// <summary>
    /// Adds accounts for a named user type, when an app would rather say which than let the generator
    /// find it.
    /// </summary>
    /// <typeparam name="TContext">The application context that owns the account tables.</typeparam>
    /// <typeparam name="TUser">The application's user entity.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options, after the <c>Rask:Auth</c> configuration section.</param>
    /// <remarks>
    /// <see cref="AuthOptions"/> reads the <c>Rask:Auth</c> configuration section first and then
    /// <paramref name="configure"/>, so code wins — and a deployed app takes its signing key and first-run
    /// token from the environment (<c>Rask__Auth__BearerSigningKey</c>) without either appearing in source.
    /// A value that is out of range, or a bearer key that cannot sign outside Development, stops the host
    /// starting. A second call registers nothing: the first call's options win, so configure it once.
    /// </remarks>
    public static IServiceCollection AddRaskAuth<TContext, TUser>(
        this IServiceCollection services, Action<AuthOptions>? configure = null)
        where TContext : DbContext
        where TUser : IdentityUser, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        // A repeat call is a no-op rather than a second Identity registration beside the first.
        if (!services.AddRaskOptions<AuthOptions>(
                "Rask:Auth", static (section, o) => section.Bind(o), configure, Validate))
        {
            return services;
        }

        // A bearer key that cannot sign keeps a Development app on cookies rather than stopping it, so a first
        // run needs no configuration; everywhere else Validate refuses to start. PostConfigure runs after the
        // section and every callback and before validation, which is exactly where that decision belongs. An
        // unknown environment counts as production: the safe default when we cannot tell is the one that refuses,
        // not the one that silently serves cookies to a caller expecting a token.
        services.AddOptions<AuthOptions>().PostConfigure<IServiceProvider>(static (options, sp) =>
        {
            if (options.Bearer
                && BearerTokens.Reject(options) is not null
                && sp.GetService<IHostEnvironment>()?.IsDevelopment() == true)
            {
                options.Bearer = false;
            }
        });

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
            .AddIdentityCore<TUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<TContext>()
            .AddSignInManager()
            // What the confirmation and reset links are minted from.
            .AddDefaultTokenProviders();

        // Every setting below is read off the BUILT AuthOptions rather than captured here: nothing is built
        // until the container is, because the Rask:Auth section cannot be read any earlier.
        services.AddOptions<IdentityOptions>().Configure<AuthOptions>(static (o, auth) =>
        {
            o.User.RequireUniqueEmail = true;

            o.Password.RequiredLength = auth.MinimumPasswordLength;
            o.Password.RequireDigit = auth.RequireMixedCasePasswords;
            o.Password.RequireLowercase = auth.RequireMixedCasePasswords;
            o.Password.RequireUppercase = auth.RequireMixedCasePasswords;
            // Length is what resists guessing; demanding punctuation mostly produces "Password1!".
            o.Password.RequireNonAlphanumeric = false;

            o.Lockout.MaxFailedAccessAttempts = auth.MaxFailedAccessAttempts;
            o.Lockout.DefaultLockoutTimeSpan = auth.LockoutDuration;
            o.Lockout.AllowedForNewUsers = true;
        });

        // One lifetime, set in one place. The email tells the reader how long the link lasts and the
        // provider decides when it stops working; read from separate settings they drift, and the
        // symptom is a message promising two hours about a token that expired in one.
        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<AuthOptions>(static (o, auth) => o.TokenLifespan = auth.TokenLifetime);

        services.TryAddScoped<AuthMail>();

        services.TryAddSingleton<IRoleSeedContexts, RoleSeedContexts<TContext>>();
        services.AddScoped<AccountService<TUser>>();

        // The endpoints resolve the store without naming the user type — MapRaskAuth() is a
        // parameterless extension method, so it has no way to know which one this app configured.
        services.TryAddScoped<IAccounts>(sp => sp.GetRequiredService<AccountService<TUser>>());

        // Whatever the Rask host adds on top: an IAuth bound to its sign-in relay, and email bodies
        // rendered from real components. Null on a host that renders none, which is the whole point of
        // this package — see AuthHost.
        //
        // BEFORE the defaults below, because every registration here is TryAdd and TryAdd is
        // FIRST-wins. Registering a default first would make the host's contribution a silent no-op:
        // the app would still start, still sign people in, and quietly send the plain-HTML emails
        // instead of its own.
        AuthHost.EnsureHostLoaded();
        AuthHost.Services?.Register<TUser>(services);

        // The bodies for the confirmation and reset emails, when the host contributed none.
        services.TryAddSingleton<IAuthEmailBodies, AuthEmailBodies>();

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
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<AuthOptions>(static (o, auth) =>
            {
                o.Cookie.Name = auth.CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax, not Strict: Strict withholds the cookie on the first navigation that arrives
                // from another site, so a visitor following a link into a protected page would land
                // signed-out and be bounced to /login despite having a valid session.
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.LoginPath = auth.LoginPath;
                o.LogoutPath = auth.LogoutPath;
                o.AccessDeniedPath = auth.AccessDeniedPath;
                o.ExpireTimeSpan = auth.ExpireTimeSpan;
                o.SlidingExpiration = auth.SlidingExpiration;
            });

        // AddRask() also calls this; it is idempotent, and Rask.Auth must not depend on being wired
        // after the host.
        services.AddAuthorization();

        AddBearer(services);

        return services;
    }

    // The values themselves, then the bearer key: a key that cannot sign is refused here — which is to say at
    // host start — everywhere but Development, where the PostConfigure above has already switched bearer off.
    private static void Validate(AuthOptions options)
    {
        options.Validate();

        if (options.Bearer && BearerTokens.Reject(options) is { } reason)
        {
            throw new InvalidOperationException(reason);
        }
    }

    /// <summary>
    ///     The bearer scheme, when an app asked for one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Added BESIDE the cookie, never instead of it: cookie stays the default scheme, so every
    ///         page, every form and every redirect behaves exactly as before and only a caller that sends
    ///         <c>Authorization: Bearer …</c> takes this path. Bearer is for the callers a cookie cannot
    ///         serve — a native client, a CLI, a service-to-service call — and a token in browser storage
    ///         is XSS-readable, which is the whole reason the cookie path exists.
    ///     </para>
    ///     <para>
    ///         Whether it is on is read off the built options, so <c>Rask:Auth:Bearer</c> can turn it on. The
    ///         handler and its post-configure are registered either way: both are inert until a Bearer scheme
    ///         exists. A misconfigured key REFUSES TO START outside Development — an operator who believes they
    ///         enabled bearer, and whose app quietly did not, is the more expensive failure, and a flag the
    ///         framework accepts and disregards is this repo's most costly bug class.
    ///     </para>
    /// </remarks>
    private static void AddBearer(IServiceCollection services)
    {
        // The guard is against AddScheme throwing "Scheme already exists" -- lazily, when the options
        // are first materialised, so the app would die on its first request naming no line to delete.
        // It is NOT a deferral: the settings below are configured last and win, the same way the cookie
        // scheme's do.
        services.AddOptions<AuthenticationOptions>().Configure<AuthOptions>(static (o, auth) =>
        {
            if (auth.Bearer && !o.SchemeMap.ContainsKey(JwtBearerDefaults.AuthenticationScheme))
            {
                o.AddScheme<JwtBearerHandler>(JwtBearerDefaults.AuthenticationScheme, displayName: null);
            }
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IPostConfigureOptions<JwtBearerOptions>, JwtBearerPostConfigureOptions>());
        services.TryAddTransient<JwtBearerHandler>();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AuthOptions>(static (o, auth) =>
            {
                // An app that wants its own JWT setup leaves AuthOptions.Bearer off, and then none of this applies.
                if (!auth.Bearer)
                {
                    return;
                }

                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.BearerIssuer,
                    ValidateAudience = true,
                    ValidAudience = auth.BearerAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.BearerSigningKey!)),
                    ValidateLifetime = true,

                    // No grace period. The default is five minutes, which quietly triples the life of a
                    // one-minute token and makes a short lifetime mean something other than it says.
                    ClockSkew = TimeSpan.Zero,
                };
            });
    }
}
