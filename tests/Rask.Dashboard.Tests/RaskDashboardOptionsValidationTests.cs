using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Dashboard.Tests;

/// <summary>
///     A bad <c>Rask:Ops</c> value fails when the options are built, naming the key it came from.
/// </summary>
public sealed class RaskDashboardOptionsValidationTests
{
    [Fact]
    public void A_page_size_below_one_is_refused_by_its_configuration_key()
    {
        var services = new ServiceCollection();
        services.AddRaskDashboard<HarnessDbContext>(o => o.PageSize = 0);
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RaskDashboardOptions>>().Value);

        Assert.Contains("Rask:Ops:PageSize must be at least 1.", error.Failures);
    }

    [Fact]
    public void Every_bad_value_is_reported_at_once()
    {
        var services = new ServiceCollection();
        services.AddRaskDashboard<HarnessDbContext>(o =>
        {
            o.RefreshInterval = TimeSpan.Zero;
            o.MaxPollDuration = TimeSpan.FromSeconds(-1);
            o.LogBufferSize = 0;
        });
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RaskDashboardOptions>>().Value);

        Assert.Equal(
            [
                "Rask:Ops:RefreshInterval must be positive.",
                "Rask:Ops:MaxPollDuration cannot be negative.",
                "Rask:Ops:LogBufferSize must be at least 1.",
            ],
            error.Failures);
    }

    [Fact]
    public void The_defaults_pass()
    {
        var services = new ServiceCollection();
        services.AddRaskDashboard<HarnessDbContext>();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RaskDashboardOptions>>().Value;

        Assert.Equal(25, options.PageSize);
    }
}
