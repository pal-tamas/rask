using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     The supervisor's policy: what it does before the process starts, and when it will not stay up.
/// </summary>
public class NodeSupervisorTests
{
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "MetaHostApp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public bool Stopped { get; private set; }

        public void StopApplication()
        {
            Stopped = true;
            _stopping.Cancel();
        }
    }

    private static NodeSupervisor Build(MetaHostingOptions options, TestLifetime lifetime) =>
        new(options, new NodeReadiness(), new TestEnvironment(), lifetime,
            NullLogger<NodeSupervisor>.Instance);

    /// <summary>
    ///     Backoff grows, then stops growing.
    /// </summary>
    /// <remarks>
    ///     Capped rather than unbounded: a process that needs longer than half a minute between
    ///     attempts is not going to be rescued by waiting longer still, and the restart budget is what
    ///     ends the loop.
    /// </remarks>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(20, 30)]
    public void Backoff_doubles_then_caps(int attempt, double expectedSeconds)
    {
        Assert.Equal(expectedSeconds, NodeSupervisor.BackoffFor(attempt).TotalSeconds);
    }

    /// <summary>
    ///     A run that stayed up resets the budget; a short one spends it.
    /// </summary>
    /// <remarks>
    ///     Without the reset the budget is a LIFETIME one, and a server crashing five times over five
    ///     months takes the host down on the fifth — which is not what "will not stay running" means.
    /// </remarks>
    [Theory]
    [InlineData(4, 90, 1)]   // lasted past the threshold: recovered, budget back to one strike
    [InlineData(4, 5, 5)]    // died quickly again: another consecutive failure
    [InlineData(0, 5, 1)]    // the very first crash
    [InlineData(4, 60, 1)]   // exactly at the threshold counts as healthy
    public void A_healthy_run_resets_the_restart_budget(int attempt, int lastedSeconds, int expected)
    {
        var next = NodeSupervisor.NextAttempt(
            attempt, TimeSpan.FromSeconds(lastedSeconds), TimeSpan.FromMinutes(1));

        Assert.Equal(expected, next);
    }

    /// <summary>
    ///     A missing server entry stops the application rather than starting a restart loop.
    /// </summary>
    /// <remarks>
    ///     The defensive path. Startup already refuses a missing entry — see
    ///     <c>SupervisorSeamTests</c> — so this covers the file disappearing after the host came up.
    /// </remarks>
    /// <remarks>
    ///     Retrying would be pure noise: the file is not going to appear, and five rounds of backoff
    ///     only delay the message that says what is actually wrong. This is the case where the front
    ///     end was never built, or <see cref="MetaHostingOptions.AppDirectory" /> points somewhere
    ///     else — both of which a person has to fix.
    /// </remarks>
    [Fact]
    public async Task A_missing_server_entry_stops_the_application()
    {
        var lifetime = new TestLifetime();
        var options = new MetaHostingOptions
        {
            AppDirectory = Path.Combine(AppContext.BaseDirectory, "no-such-frontend"),
        };

        using var supervisor = Build(options, lifetime);
        await supervisor.RunAsync(CancellationToken.None);

        Assert.True(lifetime.Stopped);
    }

    /// <summary>The entry path is resolved against the app directory, not the current directory.</summary>
    [Fact]
    public void The_server_entry_resolves_under_the_app_directory()
    {
        var options = new MetaHostingOptions
        {
            AppDirectory = "frontend",
            Framework = MetaFramework.Nuxt,
        };

        using var supervisor = Build(options, new TestLifetime());

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "frontend", ".output/server/index.mjs"),
            supervisor.ServerEntryPath);
    }

    /// <summary>
    ///     With supervision off, nothing is started and forwarding is enabled immediately.
    /// </summary>
    /// <remarks>
    ///     The escape hatch for a front end someone is running themselves. It must not consult the
    ///     server entry at all — there is no build output in that arrangement, and demanding one would
    ///     make the escape hatch unusable for the case it exists to serve.
    /// </remarks>
    [Fact]
    public async Task Supervision_can_be_turned_off_without_a_build()
    {
        var lifetime = new TestLifetime();
        var readiness = new NodeReadiness();
        var options = new MetaHostingOptions
        {
            SuperviseNode = false,
            AppDirectory = Path.Combine(AppContext.BaseDirectory, "no-such-frontend"),
        };

        using var supervisor = new NodeSupervisor(
            options, readiness, new TestEnvironment(), lifetime, NullLogger<NodeSupervisor>.Instance);
        await supervisor.RunAsync(CancellationToken.None);

        Assert.False(lifetime.Stopped);
        Assert.True(readiness.IsReady);
    }
}
