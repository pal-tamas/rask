using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Server.Files;

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

    // An out-of-range limit is refused when the options are built — at host start through ValidateOnStart, or
    // on the first resolve in a bare container like this one — and the failure names the Rask:Server section.
    [Fact]
    public void AddRask_NegativeHandlerTimeout_IsRejected() =>
        AssertRejected(o => o.HandlerTimeout = TimeSpan.FromSeconds(-1));

    [Fact]
    public void AddRask_NegativeIdleSocketTimeout_IsRejected() =>
        AssertRejected(o => o.IdleSocketTimeout = TimeSpan.FromSeconds(-1));

    [Fact]
    public void AddRask_NegativePendingHandlerBytes_IsRejected() =>
        AssertRejected(o => o.MaxPendingHandlerBytes = -1);

    [Fact]
    public void AddRask_NegativeGracePeriod_IsRejected() =>
        AssertRejected(o => o.SessionGracePeriod = TimeSpan.FromSeconds(-1));

    // 0 would abort every non-empty frame; a frame-size cap is mandatory, so it must be rejected.
    [Fact]
    public void AddRask_ZeroFrameByteCap_IsRejected() =>
        AssertRejected(o => o.MaxInboundFrameBytes = 0);

    [Fact]
    public void AddRask_NegativeFrameRateCap_IsRejected() =>
        AssertRejected(o => o.MaxInboundFramesPerSecond = -1);

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
    public void Validate_AllowsZeroForTheCountBasedCaps()
    {
        // 0 is the documented "disable this cap" value for the two count caps — Validate must accept it.
        var o = new RaskServerOptions { MaxPendingHandlers = 0, MaxInboundFramesPerSecond = 0 };
        Assert.Null(Record.Exception(o.Validate));
    }

    [Fact]
    public void Validate_RejectsANegativeShutdownDrainTimeout()
    {
        var o = new RaskServerOptions { ShutdownDrainTimeout = TimeSpan.FromSeconds(-1) };

        Assert.Throws<ArgumentOutOfRangeException>(o.Validate);
    }

    [Fact]
    public void Validate_RejectsAShutdownDrainTimeoutCancelAfterCannotTake()
    {
        // CancellationTokenSource.CancelAfter throws above int.MaxValue milliseconds — and it would
        // throw from the shutdown path, the worst possible place to find out.
        var o = new RaskServerOptions { ShutdownDrainTimeout = TimeSpan.FromDays(30) };

        Assert.Throws<ArgumentOutOfRangeException>(o.Validate);
    }

    [Fact]
    public void Validate_AllowsZeroToDisableTheDrain()
    {
        // The documented opt-out: Zero restores the pre-drain behaviour of aborting immediately.
        var o = new RaskServerOptions { ShutdownDrainTimeout = TimeSpan.Zero };

        Assert.Null(Record.Exception(o.Validate));
    }

    [Fact]
    public void ShutdownDrainTimeout_FlowsIntoThePerHostLimits()
    {
        var services = new ServiceCollection()
            .AddRask(configureServer: o => o.ShutdownDrainTimeout = TimeSpan.FromSeconds(3));

        using var provider = services.BuildServiceProvider();
        var limits = provider.GetRequiredService<RaskServerLimits>();

        Assert.Equal(TimeSpan.FromSeconds(3), limits.ShutdownDrainTimeout);
    }

    [Fact]
    public void TheRaskServerSection_SeedsThePerHostLimits()
    {
        using var provider = Provider(new()
        {
            ["Rask:Server:MaxInboundFramesPerSecond"] = "250",
            ["Rask:Server:MaxPendingHandlers"] = "64",
            ["Rask:Server:SessionGracePeriod"] = "00:00:15",
            ["Rask:Server:RenderModes:QuiescenceTimeout"] = "00:00:02",
        });

        var limits = provider.GetRequiredService<RaskServerLimits>();

        Assert.Equal(250, limits.MaxInboundFramesPerSecond);
        Assert.Equal(64, limits.MaxPendingHandlers);
        Assert.Equal(TimeSpan.FromSeconds(15), limits.SessionGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(2), limits.InitialRenderQuiescenceTimeout);
    }

    [Fact]
    public void ConfigureServer_WinsOverTheRaskServerSection()
    {
        using var provider = Provider(
            new() { ["Rask:Server:MaxPendingHandlers"] = "64", ["Rask:Server:MaxInboundFramesPerSecond"] = "250" },
            server: o => o.MaxPendingHandlers = 7);

        var limits = provider.GetRequiredService<RaskServerLimits>();

        Assert.Equal(7, limits.MaxPendingHandlers);
        Assert.Equal(250, limits.MaxInboundFramesPerSecond);
    }

    [Fact]
    public void AnOutOfRangeConfiguredLimit_IsRejectedNamingTheSection()
    {
        using var provider = Provider(new() { ["Rask:Server:SessionGracePeriod"] = "-00:00:01" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<RaskServerLimits>());
        Assert.Contains("Rask:Server", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFlatRaskSection_IsNotRead()
    {
        // The shape docs/configuration.md used to teach, before every host had its own Rask:<Area> section.
        using var provider = Provider(new() { ["Rask:MaxPendingHandlers"] = "64" });

        Assert.Equal(512, provider.GetRequiredService<RaskServerLimits>().MaxPendingHandlers);
    }

    // The session store is IAsyncDisposable only, so the containers that resolve it are disposed asynchronously.
    [Fact]
    public async Task TheRaskLiveSection_ReachesTheSessionStore()
    {
        await using var provider = Provider(new()
        {
            ["Rask:Live:MaxSessions"] = "5",
            ["Rask:Live:DiffMode"] = nameof(LiveDiffMode.DisabledFull),
        });

        var store = provider.GetRequiredService<LiveSessionStore>();

        Assert.Equal(5, store.MaxSessions);
        Assert.Equal(LiveDiffMode.DisabledFull, store.DiffMode);
    }

    [Fact]
    public async Task Configure_WinsOverTheRaskLiveSection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration(new() { ["Rask:Live:MaxSessions"] = "5" }));
        services.AddRask(configure: o => o.MaxSessions = 9);
        await using var provider = services.BuildServiceProvider();

        Assert.Equal(9, provider.GetRequiredService<LiveSessionStore>().MaxSessions);
    }

    [Fact]
    public void TheRaskUploadsSection_SetsTheUploadLimits()
    {
        using var provider = Provider(new() { ["Rask:Uploads:MaxFileSize"] = "1024" });

        Assert.Equal(1024, provider.GetRequiredService<RaskUploadOptions>().MaxFileSize);
    }

    [Fact]
    public void TheRaskCultureSection_SetsTheCultures()
    {
        using var provider = Provider(new()
        {
            ["Rask:Culture:SupportedCultures:0"] = "en",
            ["Rask:Culture:SupportedCultures:1"] = "hu",
            ["Rask:Culture:UseCookie"] = "false",
        });

        var culture = provider.GetRequiredService<RaskCultureOptions>();

        Assert.Equal(["en", "hu"], culture.SupportedCultures);
        Assert.False(culture.UseCookie);
    }

    [Fact]
    public async Task WithoutConfiguration_AddRaskStillBuilds()
    {
        // A bare container — a test fixture, a benchmark harness — has no IConfiguration at all.
        await using var provider = new ServiceCollection().AddRask().BuildServiceProvider();

        Assert.Equal(0, provider.GetRequiredService<LiveSessionStore>().MaxSessions);
        Assert.Empty(provider.GetRequiredService<RaskCultureOptions>().SupportedCultures);
    }

    private static void AssertRejected(Action<RaskServerOptions> configureServer)
    {
        using var provider = new ServiceCollection().AddRask(configureServer: configureServer).BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<RaskServerLimits>());
        Assert.Contains("Rask:Server", ex.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Provider(
        Dictionary<string, string?> settings, Action<RaskServerOptions>? server = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration(settings));
        services.AddRask(configureServer: server);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
