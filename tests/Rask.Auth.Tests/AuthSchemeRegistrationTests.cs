using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Auth.Tests;

/// <summary>
///     The battery owns the cookie scheme: it registers one whatever the app did, and its settings win.
/// </summary>
/// <remarks>
///     Cookies are the only session Rask authenticates, so the battery no longer stands down when the app
///     has wired authentication of its own. Registering the scheme entry twice is not a no-op —
///     <c>AuthenticationOptions.AddScheme</c> throws "Scheme already exists", lazily, when the options are
///     first materialised — so an app still carrying a hand-written <c>AddAuthentication().AddCookie()</c>
///     must not be turned into a startup crash. Owning the scheme means the battery's configuration wins,
///     not that the app fails to start.
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class AuthSchemeRegistrationTests
{
    [Fact]
    public void An_app_with_no_scheme_of_its_own_gets_the_cookie_one()
    {
        using var provider = Build();

        var schemes = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes;

        Assert.Contains(schemes, s => s.Name == CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public void An_app_that_already_registered_a_cookie_scheme_still_starts()
    {
        using var provider = Build(o => o.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(c => c.LoginPath = "/sign-in"));

        // Materialising AuthenticationOptions is where a duplicate registration throws, so resolving it
        // at all is most of the assertion.
        var schemes = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes;

        Assert.Single(schemes, s => s.Name == CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public void The_batterys_cookie_settings_win_over_the_apps_own()
    {
        using var provider = Build(o => o.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(c => c.LoginPath = "/sign-in"));

        var cookie = Cookie(provider);

        // The battery's default, not the app's /sign-in. The battery configures the named options last,
        // which is what "Rask.Auth owns the cookie scheme" has to mean to be worth anything: /login is
        // where the built-in page lives and where the guards redirect.
        Assert.Equal("/login", cookie.LoginPath);
    }

    [Fact]
    public void The_cookie_scheme_is_the_default_even_when_the_app_named_another_one()
    {
        using var provider = Build(o => o.AddAuthentication("other")
            .AddCookie("other", c => c.LoginPath = "/elsewhere"));

        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        // An external provider composes by adding a CHALLENGE scheme beside this one; the cookie stays
        // the scheme IAuthSignIn drives and the redeem endpoint writes.
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, options.DefaultScheme);
        Assert.Contains(options.Schemes, s => s.Name == "other");
    }

    [Fact]
    public void An_app_can_still_configure_the_cookie_after_the_battery()
    {
        using var provider = Build(configureAfter: services => services
            .Configure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme, o => o.Cookie.Domain = ".example.com"));

        var cookie = Cookie(provider);

        // Owning the scheme is not owning every knob: AuthOptions does not carry a cookie domain, and
        // configuring the same named options after AddRaskAuth is the escape hatch that remains.
        Assert.Equal(".example.com", cookie.Cookie.Domain);
        Assert.Equal("/login", cookie.LoginPath);
    }

    [Fact]
    public void An_external_provider_added_after_the_battery_composes_with_the_cookie()
    {
        // A second cookie scheme under another name stands in for AddOpenIdConnect, so this suite does
        // not take a dependency on the OIDC package to assert a shape that is about scheme wiring: an
        // external provider performs the CHALLENGE and signs in through the cookie the battery owns.
        // This is the arrangement docs/authentication-providers.md shows, in the order it shows it.
        using var provider = Build(configureAfter: services =>
        {
            services.AddAuthentication().AddCookie("idp", c => c.LoginPath = "/idp");
            services.Configure<AuthenticationOptions>(o => o.DefaultChallengeScheme = "idp");
        });

        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, options.DefaultScheme);
        Assert.Equal("idp", options.DefaultChallengeScheme);
        Assert.Contains(options.Schemes, s => s.Name == "idp");
        // The battery keeps configuring its own cookie; adding a provider does not disturb it.
        Assert.Equal("/login", Cookie(provider).LoginPath);
    }

    private static CookieAuthenticationOptions Cookie(IServiceProvider provider) =>
        provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

    private static ServiceProvider Build(
        Action<IServiceCollection>? configureBefore = null,
        Action<IServiceCollection>? configureAfter = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite("Data Source=:memory:"));

        configureBefore?.Invoke(services);
        services.AddRaskAuth<AuthDbContext>();
        configureAfter?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
