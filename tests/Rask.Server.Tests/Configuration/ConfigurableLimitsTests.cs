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
            RaskEndpointExtensions.UnconnectedSessionGracePeriod,
            RaskEndpointExtensions.IdleSocketTimeout,
            RaskEndpointExtensions.MaxPendingHandlerBytes,
            RaskEndpointExtensions.HandlerTimeout);
        try
        {
            new ServiceCollection().AddRask(configureServer: o =>
            {
                o.MaxInboundFrameBytes = 1234;
                o.MaxPendingHandlers = 7;
                o.MaxInboundFramesPerSecond = 42;
                o.SessionGracePeriod = TimeSpan.FromSeconds(5);
                o.UnconnectedSessionGracePeriod = TimeSpan.FromSeconds(2);
                o.IdleSocketTimeout = TimeSpan.FromSeconds(90);
                o.MaxPendingHandlerBytes = 4096;
                o.HandlerTimeout = TimeSpan.FromSeconds(7);
            });

            Assert.Equal(1234, RaskEndpointExtensions.MaxInboundFrameBytes);
            Assert.Equal(7, RaskEndpointExtensions.MaxPendingHandlers);
            Assert.Equal(42, RaskEndpointExtensions.MaxInboundFramesPerSecond);
            Assert.Equal(TimeSpan.FromSeconds(5), RaskEndpointExtensions.SessionGracePeriod);
            Assert.Equal(TimeSpan.FromSeconds(2), RaskEndpointExtensions.UnconnectedSessionGracePeriod);
            Assert.Equal(TimeSpan.FromSeconds(90), RaskEndpointExtensions.IdleSocketTimeout);
            Assert.Equal(4096, RaskEndpointExtensions.MaxPendingHandlerBytes);
            Assert.Equal(TimeSpan.FromSeconds(7), RaskEndpointExtensions.HandlerTimeout);
        }
        finally
        {
            (RaskEndpointExtensions.MaxInboundFrameBytes,
                RaskEndpointExtensions.MaxPendingHandlers,
                RaskEndpointExtensions.MaxInboundFramesPerSecond,
                RaskEndpointExtensions.SessionGracePeriod,
                RaskEndpointExtensions.UnconnectedSessionGracePeriod,
                RaskEndpointExtensions.IdleSocketTimeout,
                RaskEndpointExtensions.MaxPendingHandlerBytes,
                RaskEndpointExtensions.HandlerTimeout) = saved;
        }
    }

    [Fact]
    public void AddRask_NegativeHandlerTimeout_ThrowsAtStartup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddRask(configureServer: o => o.HandlerTimeout = TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AddRask_NegativeIdleSocketTimeout_ThrowsAtStartup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddRask(configureServer: o => o.IdleSocketTimeout = TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AddRask_NegativePendingHandlerBytes_ThrowsAtStartup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddRask(configureServer: o => o.MaxPendingHandlerBytes = -1));
    }

    [Fact]
    public void AddRask_NoConfigureServer_DoesNotResetStaticLimits()
    {
        var saved = RaskEndpointExtensions.MaxInboundFramesPerSecond;
        try
        {
            // Set a sentinel, then a bare AddRask() must leave it untouched (it would catch a
            // regression that reset the statics to defaults on the no-callback path).
            RaskEndpointExtensions.MaxInboundFramesPerSecond = 777;
            new ServiceCollection().AddRask();
            Assert.Equal(777, RaskEndpointExtensions.MaxInboundFramesPerSecond);
        }
        finally
        {
            RaskEndpointExtensions.MaxInboundFramesPerSecond = saved;
        }
    }

    [Fact]
    public void AddRask_NegativeGracePeriod_ThrowsAtStartup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddRask(configureServer: o => o.SessionGracePeriod = TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AddRask_ZeroFrameByteCap_ThrowsAtStartup()
    {
        // 0 would abort every non-empty frame; a frame-size cap is mandatory, so it must be rejected.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddRask(configureServer: o => o.MaxInboundFrameBytes = 0));
    }

    [Fact]
    public void AddRask_NegativeFrameRateCap_ThrowsAtStartup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddRask(configureServer: o => o.MaxInboundFramesPerSecond = -1));
    }

    [Fact]
    public void Validate_AllowsZeroForTheCountBasedCaps()
    {
        // 0 is the documented "disable this cap" value for the two count caps — Validate must accept it.
        // Tested directly (not via AddRask) so it doesn't seed the process-global statics.
        var o = new RaskServerOptions { MaxPendingHandlers = 0, MaxInboundFramesPerSecond = 0 };
        Assert.Null(Record.Exception(o.Validate));
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
