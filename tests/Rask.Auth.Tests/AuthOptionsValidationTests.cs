using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Auth.Tests;

/// <summary>A value that cannot produce a working app stops it starting, and the failure names the setting.</summary>
[Collection(AuthDbCollection.Name)]
public sealed class AuthOptionsValidationTests
{
    [Fact]
    public void An_empty_path_is_refused_naming_the_setting()
    {
        using var provider = Build(o => o.LoginPath = " ");

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<AuthOptions>());

        Assert.Contains("LoginPath", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_lifetime_that_is_not_positive_is_refused_naming_the_setting()
    {
        using var provider = Build(o => o.ExpireTimeSpan = TimeSpan.Zero);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<AuthOptions>());

        Assert.Contains("ExpireTimeSpan", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relying_party_id_with_a_scheme_is_refused_naming_the_setting()
    {
        using var provider = Build(o => o.PasskeyRelyingPartyId = "https://example.com");

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<AuthOptions>());

        Assert.Contains("PasskeyRelyingPartyId", error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(Action<AuthOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        services.AddRaskAuth<AuthDbContext>(configure);
        return services.BuildServiceProvider();
    }
}
