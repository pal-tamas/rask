using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Auth.Tests;

/// <summary>
///     The battery registers a cookie scheme only when the app has not registered one itself.
/// </summary>
/// <remarks>
///     Registering a scheme twice is not a no-op: <c>AuthenticationOptions.AddScheme</c> throws
///     "Scheme already exists". Because auth is on by default, a battery that always registered one
///     would break at <b>startup</b> for two ordinary apps — one bringing its own OIDC or JWT scheme
///     (the pattern <c>docs/authentication-providers.md</c> documents), and one still carrying a
///     hand-written <c>AddAuthentication().AddCookie()</c> from before the battery existed.
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class AuthSchemeRegistrationTests
{
    [Fact]
    public void An_app_with_no_scheme_of_its_own_gets_the_cookie_one()
    {
        using var provider = Build(configureAuthentication: false);

        var schemes = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes;

        Assert.Contains(schemes, s => s.Name == CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public void An_app_that_already_registered_a_cookie_scheme_still_starts()
    {
        using var provider = Build(configureAuthentication: true);

        // Materialising AuthenticationOptions is where a duplicate registration throws, so resolving it
        // at all is most of the assertion.
        var schemes = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes;

        Assert.Single(schemes, s => s.Name == CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public void An_app_that_brought_its_own_scheme_keeps_its_own_settings()
    {
        using var provider = Build(configureAuthentication: true);

        var cookie = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        // The app's value, not the battery's default of /login. Deferring means deferring completely:
        // half-applying the battery's options over an app's own scheme would be worse than either.
        Assert.Equal("/sign-in", cookie.LoginPath);
    }

    private static ServiceProvider Build(bool configureAuthentication)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite("Data Source=:memory:"));

        if (configureAuthentication)
        {
            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o => o.LoginPath = "/sign-in");
        }

        services.AddRaskAuth<AuthDbContext>();

        return services.BuildServiceProvider();
    }
}
