using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.Configuration;

// No shared global state to serialize on any more — each test builds its own provider and reads the
// per-host RaskServerLimits singleton, so this class runs in parallel with the rest of the suite.
public class ConfigurableLimitsTests
{
    [Fact]
    public void RaskServerOptions_Defaults_MatchTheShippedDefaults()
    {
        var o = new RaskServerOptions();

        Assert.Equal(8 * 1024 * 1024, o.MaxInboundFrameBytes);
        Assert.Equal(512, o.MaxPendingHandlers);
        Assert.Equal(1000, o.MaxInboundFramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(30), o.SessionGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(10), o.UnconnectedSessionGracePeriod);
    }

    [Fact]
    public void AddRask_ConfigureServer_SeedsThePerHostLimits()
    {
        // configureServer projects RaskServerOptions into the per-host RaskServerLimits singleton —
        // no process-global statics, so this is fully isolated from every other test.
        using var provider = new ServiceCollection().AddRask(configureServer: o =>
        {
            o.MaxInboundFrameBytes = 1234;
            o.MaxPendingHandlers = 7;
            o.MaxInboundFramesPerSecond = 42;
            o.SessionGracePeriod = TimeSpan.FromSeconds(5);
            o.UnconnectedSessionGracePeriod = TimeSpan.FromSeconds(2);
            o.IdleSocketTimeout = TimeSpan.FromSeconds(90);
            o.MaxPendingHandlerBytes = 4096;
            o.HandlerTimeout = TimeSpan.FromSeconds(7);
        }).BuildServiceProvider();

        var limits = provider.GetRequiredService<RaskServerLimits>();

        Assert.Equal(1234, limits.MaxInboundFrameBytes);
        Assert.Equal(7, limits.MaxPendingHandlers);
        Assert.Equal(42, limits.MaxInboundFramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(5), limits.SessionGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(2), limits.UnconnectedSessionGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(90), limits.IdleSocketTimeout);
        Assert.Equal(4096, limits.MaxPendingHandlerBytes);
        Assert.Equal(TimeSpan.FromSeconds(7), limits.HandlerTimeout);
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
    public void AddRask_NoConfigureServer_RegistersDefaultLimits()
    {
        // A bare AddRask() registers a RaskServerLimits carrying the framework defaults.
        using var provider = new ServiceCollection().AddRask().BuildServiceProvider();
        var limits = provider.GetRequiredService<RaskServerLimits>();

        Assert.Equal(8 * 1024 * 1024, limits.MaxInboundFrameBytes);
        Assert.Equal(512, limits.MaxPendingHandlers);
        Assert.Equal(1000, limits.MaxInboundFramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.SessionGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(10), limits.UnconnectedSessionGracePeriod);
        Assert.Equal(TimeSpan.Zero, limits.IdleSocketTimeout);
        Assert.Equal(TimeSpan.Zero, limits.HandlerTimeout);
        Assert.Equal(0, limits.MaxPendingHandlerBytes);
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
