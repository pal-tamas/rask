using Microsoft.Extensions.Configuration;
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
    public void A_host_nobody_gave_a_budget_keeps_the_fifteen_seconds_rung()
    {
        var options = Resolve();

        // With no budget named — a host not started by `rask deploy`, or by an older one that SIGKILLed at 20s —
        // the app keeps the old rung. `rask deploy` names one from stopTimeout; the test below keeps it.
        Assert.Equal(TimeSpan.FromSeconds(15), options.ShutdownTimeout);
    }

    [Fact]
    public void The_host_stops_the_hosted_services_concurrently()
    {
        // The half that makes the arithmetic work. Sequentially the pillars' graces SUM — Litestream's WAL
        // flush 10s + an in-flight email 10s + a job 5s + an outbox item 5s = 30s against a 15s budget — so
        // whichever stops last gets none of it, decided by the order of the AddRaskX calls in Program.cs.
        // Concurrently they overlap at max(...) = 10s.
        Assert.True(Resolve().ServicesStopConcurrently);
    }

    [Fact]
    public void A_budget_already_chosen_before_AddRask_is_left_alone()
    {
        // The generic host binds HostOptions from configuration BEFORE AddRask runs, which is how
        // ASPNETCORE_SHUTDOWNTIMEOUTSECONDS and the shutdownTimeoutSeconds key reach it. Writing the
        // default unconditionally would silently discard both: an operator who raised the budget for a
        // long drain would get 15s and no warning. Rask only replaces the value while it still holds
        // .NET's own 30s default.
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(42));
        services.AddRask();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(42), options.ShutdownTimeout);
        Assert.True(options.ServicesStopConcurrently); // still applied — it has no config binding to defer to
    }

    [Fact]
    public void A_budget_the_deploy_names_is_kept_even_when_it_equals_the_framework_default()
    {
        // `rask deploy` hands the app its budget as ASPNETCORE_SHUTDOWNTIMEOUTSECONDS. A stopTimeout of 35s makes
        // that 30 — .NET's own default — which the value check alone would mistake for "nobody chose" and cut to 15.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SHUTDOWNTIMEOUTSECONDS"] = "30" })
            .Build());
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        services.AddRask();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(30), options.ShutdownTimeout);
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
