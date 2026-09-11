using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Api.Tests;

// ApiOptions come from Rask:Api first and the AddRaskApi callback second. A prefix the setter refuses is reported as a
// validation failure naming the section — the value may well have come from appsettings rather than code.
public sealed class ApiOptionsBindingTests
{
    [Fact]
    public void The_Rask_Api_section_sets_the_options()
    {
        using var provider = Provider(new()
        {
            ["Rask:Api:Prefix"] = "/services",
            ["Rask:Api:NotFound"] = "false",
        });

        var options = provider.GetRequiredService<ApiOptions>();

        Assert.Equal("/services", options.Prefix);
        Assert.False(options.NotFound);
    }

    [Fact]
    public void A_prefix_the_setter_refuses_is_reported_naming_the_section()
    {
        using var provider = Provider(new() { ["Rask:Api:Prefix"] = "services" });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ApiOptions>());
        Assert.Contains("Rask:Api", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_callback_wins_over_the_section()
    {
        using var provider = Provider(new() { ["Rask:Api:Prefix"] = "/services" }, o => o.Prefix = "/v2");

        Assert.Equal("/v2", provider.GetRequiredService<ApiOptions>().Prefix);
    }

    [Fact]
    public void A_top_level_Api_section_is_not_read()
    {
        using var provider = Provider(new() { ["Api:Prefix"] = "/services" });

        Assert.Equal("/api", provider.GetRequiredService<ApiOptions>().Prefix);
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings, Action<ApiOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskApi(configure);
        return services.BuildServiceProvider();
    }
}
