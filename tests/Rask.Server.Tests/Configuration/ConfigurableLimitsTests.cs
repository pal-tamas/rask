using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.Configuration;

// Serialized with the other tests that read/write the process-global WS/grace-period statics.
[Collection("SessionGracePeriod")]
public class ConfigurableLimitsTests
{
    [Fact]
    public void RaskServerOptions_Defaults_MatchTheShippedStaticLimits()
    {
        var o = new RaskServerOptions();

        Assert.Equal(8 * 1024 * 1024, o.MaxInboundFrameBytes);
        Assert.Equal(512, o.MaxPendingHandlers);
        Assert.Equal(1000, o.MaxInboundFramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(30), o.SessionGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(10), o.UnconnectedSessionGracePeriod);
    }

    [Fact]
    public void AddRask_ConfigureServer_SeedsTheServerStaticLimits()
    {
        var saved = (
            RaskEndpointExtensions.MaxInboundFrameBytes,
            RaskEndpointExtensions.MaxPendingHandlers,
            RaskEndpointExtensions.MaxInboundFramesPerSecond,
            RaskEndpointExtensions.SessionGracePeriod,
            RaskEndpointExtensions.UnconnectedSessionGracePeriod);
        try
        {
            new ServiceCollection().AddRask(configureServer: o =>
            {
                o.MaxInboundFrameBytes = 1234;
                o.MaxPendingHandlers = 7;
                o.MaxInboundFramesPerSecond = 42;
                o.SessionGracePeriod = TimeSpan.FromSeconds(5);
                o.UnconnectedSessionGracePeriod = TimeSpan.FromSeconds(2);
            });

            Assert.Equal(1234, RaskEndpointExtensions.MaxInboundFrameBytes);
            Assert.Equal(7, RaskEndpointExtensions.MaxPendingHandlers);
            Assert.Equal(42, RaskEndpointExtensions.MaxInboundFramesPerSecond);
            Assert.Equal(TimeSpan.FromSeconds(5), RaskEndpointExtensions.SessionGracePeriod);
            Assert.Equal(TimeSpan.FromSeconds(2), RaskEndpointExtensions.UnconnectedSessionGracePeriod);
        }
        finally
        {
            (RaskEndpointExtensions.MaxInboundFrameBytes,
                RaskEndpointExtensions.MaxPendingHandlers,
                RaskEndpointExtensions.MaxInboundFramesPerSecond,
                RaskEndpointExtensions.SessionGracePeriod,
                RaskEndpointExtensions.UnconnectedSessionGracePeriod) = saved;
        }
    }

    [Fact]
    public void AddRask_NoConfigureServer_LeavesStaticLimitsAtTheirCurrentValue()
    {
        var before = RaskEndpointExtensions.MaxInboundFramesPerSecond;
        new ServiceCollection().AddRask();
        Assert.Equal(before, RaskEndpointExtensions.MaxInboundFramesPerSecond);
    }

    [Fact]
    public void AppSettings_BindIntoServerOptions_RoundTrips()
    {
        // The documented operator pattern: AddRask(configureServer: o => config.GetSection("Rask").Bind(o)).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rask:MaxInboundFramesPerSecond"] = "250",
                ["Rask:MaxPendingHandlers"] = "64",
                ["Rask:SessionGracePeriod"] = "00:00:15"
            })
            .Build();

        var o = new RaskServerOptions();
        config.GetSection("Rask").Bind(o);

        Assert.Equal(250, o.MaxInboundFramesPerSecond);
        Assert.Equal(64, o.MaxPendingHandlers);
        Assert.Equal(TimeSpan.FromSeconds(15), o.SessionGracePeriod);
    }
}
