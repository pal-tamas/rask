using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Rask.Auth.Tests;

/// <summary>
///     Bearer tokens: opt-in, configured key, no refresh (#1010).
/// </summary>
/// <remarks>
///     <para>
///         Cookie stays the default everywhere. Bearer is for the callers a cookie cannot serve — a
///         native client, a CLI, a service-to-service call — and a token in browser storage is
///         XSS-readable, which is exactly why the cookie path exists for anything running in a page.
///     </para>
///     <para>
///         The three decisions this feature was blocked on are each pinned by a test below, because each
///         is a security posture rather than a preference: where the key comes from, what its absence
///         means outside Development, and whether there is a refresh token.
///     </para>
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class BearerTokenTests
{
    private const string GoodKey = "a-signing-key-long-enough-for-hmac-sha256!";

    // ---------- decision 1: the key comes from configuration ----------

    [Fact]
    public void Bearer_is_off_unless_the_app_asks_for_it()
    {
        // The default has to be cookie-only: bearer is a configured deviation, and an app that never
        // mentioned it must not acquire a second way to authenticate.
        using var provider = Build();

        var schemes = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes;

        Assert.DoesNotContain(schemes, s => s.Name == JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void Turning_it_on_adds_the_scheme_beside_the_cookie_rather_than_instead_of_it()
    {
        using var provider = Build(o =>
        {
            o.Bearer = true;
            o.BearerSigningKey = GoodKey;
        });

        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Contains(options.Schemes, s => s.Name == JwtBearerDefaults.AuthenticationScheme);

        // Still the cookie, so every page, form and redirect behaves exactly as before and only a
        // caller sending Authorization: Bearer takes the new path.
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, options.DefaultScheme);
    }

    // ---------- decision 2: absence outside Development refuses to start ----------

    [Fact]
    public void No_key_outside_development_refuses_to_start()
    {
        // The deliberate half. An operator who believes they enabled bearer, and whose app quietly did
        // not, is the more expensive failure — and a flag the framework accepts and disregards is this
        // repo's most costly bug class. Same reasoning as MailOptions.From throwing.
        var error = Assert.Throws<InvalidOperationException>(() => Build(o => o.Bearer = true));

        Assert.Contains("BearerSigningKey", error.Message, StringComparison.Ordinal);
        Assert.Contains("configuration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_too_short_to_sign_with_refuses_to_start_as_well()
    {
        // Rejected at startup rather than from inside the token handler on the first sign-in, which is
        // a far worse place to find out: the app is up, and one caller gets a 500.
        var error = Assert.Throws<InvalidOperationException>(() => Build(o =>
        {
            o.Bearer = true;
            o.BearerSigningKey = "too-short";
        }));

        Assert.Contains("32", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_development_a_missing_key_warns_and_leaves_the_app_on_cookies()
    {
        // A first run needs no configuration at all. It must not silently look like bearer works,
        // though — Bearer is turned back off, so nothing issues a token.
        using var provider = Build(o => o.Bearer = true, environment: "Development");

        Assert.False(provider.GetRequiredService<AuthOptions>().Bearer);
        Assert.DoesNotContain(
            provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes,
            s => s.Name == JwtBearerDefaults.AuthenticationScheme);
    }

    // ---------- decision 3: a short access token, and no refresh ----------

    [Fact]
    public void The_issued_token_carries_the_principal_and_expires()
    {
        var options = new AuthOptions
        {
            Bearer = true,
            BearerSigningKey = GoodKey,
            BearerLifetime = TimeSpan.FromMinutes(30),
        };

        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(start);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "u1"), new Claim(ClaimTypes.Role, "admin")],
            "test"));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            BearerTokens.Issue(principal, options, clock));

        Assert.Equal(options.BearerIssuer, token.Issuer);
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == "u1");
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Role && c.Value == "admin");

        // Thirty minutes, not the five-minute grace the validator would otherwise add on top: the
        // lifetime has to mean what it says.
        Assert.Equal(start.UtcDateTime.AddMinutes(30), token.ValidTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void There_is_no_refresh_token_on_the_surface()
    {
        // A decision, not an omission: refresh needs a revocation story, revocation needs storage, and
        // that is a much larger feature. This test exists so adding one is a deliberate act rather than
        // something that quietly appears.
        var names = typeof(Rask.Core.Authentication.BearerSession)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("AccessToken", names);
        Assert.Contains("ExpiresIn", names);
    }

    [Fact]
    public void The_validator_allows_no_clock_skew()
    {
        using var provider = Build(o =>
        {
            o.Bearer = true;
            o.BearerSigningKey = GoodKey;
            o.BearerLifetime = TimeSpan.FromMinutes(1);
        });

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        // The default is five minutes, which would quietly make a one-minute token last six.
        Assert.Equal(TimeSpan.Zero, jwt.TokenValidationParameters.ClockSkew);
    }

    private static ServiceProvider Build(Action<AuthOptions>? configure = null, string? environment = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite("Data Source=:memory:"));

        if (environment is not null)
        {
            // Registered as an INSTANCE, which is how a host registers it and how AddRaskAuth reads it
            // back without building a second container.
            services.AddSingleton<IHostEnvironment>(new StubEnvironment(environment));
        }

        services.AddRaskAuth<AuthDbContext>(o => configure?.Invoke(o));

        return services.BuildServiceProvider();
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
