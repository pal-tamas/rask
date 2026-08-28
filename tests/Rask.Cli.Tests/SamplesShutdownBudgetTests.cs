using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.Server;

namespace Rask.Cli.Tests;

/// <summary>
///     The shutdown budget an app actually gets has to fit the window <c>rask deploy</c> allows.
/// </summary>
/// <remarks>
///     <para>
///         This used to scan every sample for a hand-written <c>Configure&lt;HostOptions&gt;</c> block,
///         because that was the only way any app had one — and the reason it was written is that nine of
///         the ten web hosts had inherited .NET's default 30s <c>ShutdownTimeout</c>, which <b>exceeds</b>
///         the 20s <c>rask deploy</c> allows between SIGTERM and SIGKILL. A sample deployed as written was
///         killed mid-shutdown, and a reader copying from any of the nine inherited the wrong lesson.
///     </para>
///     <para>
///         <c>AddRask</c> applies the budget now, so the scan is gone and the assertion moved to the thing
///         that was always the point: the options an app ends up with. Checking the source text would only
///         prove the samples still contain a block they no longer need.
///     </para>
/// </remarks>
public class SamplesShutdownBudgetTests
{
    private static HostOptions Resolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRask();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HostOptions>>().Value;
    }

    [Fact]
    public void AddRask_budgets_the_shutdown_inside_the_deploy_window()
    {
        // The two numbers are owned by different assemblies on purpose — Rask.Cli passes `docker stop -t`
        // and must not reference the framework — so this is what holds them together. Asserted as a
        // relationship rather than a literal: either may move, but the app must always give up first.
        var options = Resolve();

        Assert.True(
            options.ShutdownTimeout < TimeSpan.FromSeconds(ShutdownBudget.DockerStopSeconds),
            $"the host budgets {options.ShutdownTimeout.TotalSeconds}s, but `rask deploy` SIGKILLs at "
            + $"{ShutdownBudget.DockerStopSeconds}s — the app would be killed mid-shutdown");
    }

    [Fact]
    public void AddRask_stops_the_hosted_services_concurrently()
    {
        // The half that makes the arithmetic work. Sequentially the pillars' graces SUM — Litestream's WAL
        // flush 10s + an in-flight email 10s + a job 5s + an outbox item 5s = 30s against a 15s budget — so
        // whichever stops last gets none of it, decided by the order of the AddRaskX calls in Program.cs.
        // Concurrently they overlap at max(...) = 10s.
        Assert.True(Resolve().ServicesStopConcurrently);
    }

    [Fact]
    public void An_app_can_still_choose_its_own_budget()
    {
        // Rask picks a default; it does not overrule a decision. Options setups run in registration order,
        // so an app configuring HostOptions after AddRask is the last writer.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRask();
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(45));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(45), options.ShutdownTimeout);
        Assert.True(options.ServicesStopConcurrently); // untouched by the override
    }
}
