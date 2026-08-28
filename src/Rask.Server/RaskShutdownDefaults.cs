using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Rask.Server;

/// <summary>
/// The shutdown budget <c>AddRask</c> applies to <see cref="HostOptions"/>, so an app finishes stopping
/// before the container runtime loses patience instead of being killed mid-write.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="HostOptions.ServicesStopConcurrently"/> is the load-bearing half.</b> Stopped one at a
/// time — .NET's default — each hosted service's own shutdown grace <em>sums</em> inside the one budget:
/// Litestream's final WAL flush (10s), an in-flight email send (10s), a running job and an outbox item (5s
/// each) add up to 30s, and whichever service stops last gets none of it. Which one that is depends on the
/// order of the <c>AddRaskX</c> calls in someone's <c>Program.cs</c>, so the symptom is a truncated write
/// that moves when you reorder two unrelated lines. Stopped concurrently they overlap at
/// <c>max(…) = 10s</c>, which fits the budget with room to spare.
/// </para>
/// <para>
/// The timeout is sized to <c>rask deploy</c>, which sends SIGTERM and SIGKILLs 20 seconds later; the
/// margin covers what happens after <c>StopAsync</c> returns — container teardown, log flush, DI disposal,
/// EF connection teardown. It is a sensible budget for any host, and an app that needs a different one
/// configures <see cref="HostOptions"/> <em>after</em> <c>AddRask</c>: options setups run in registration
/// order, so the last writer decides.
/// </para>
/// </remarks>
internal sealed class RaskShutdownDefaults : IConfigureOptions<HostOptions>
{
    /// <summary>Seconds <c>rask deploy</c>'s <c>docker stop -t</c> waits after SIGTERM before SIGKILL.</summary>
    /// <remarks>
    /// Mirrored by <c>Rask.Cli</c>'s <c>ShutdownBudget.DockerStopSeconds</c>, which is what actually passes
    /// the flag. The two are held together by a test rather than by this comment — see
    /// <c>Rask.Cli.Tests.SamplesShutdownBudgetTests</c>, which resolves the options this type produces and
    /// checks them against the deploy ladder.
    /// </remarks>
    internal const int DockerStopSeconds = 20;

    /// <summary>Margin between the app finishing and the SIGKILL landing.</summary>
    internal const int HostReserveSeconds = 5;

    /// <summary>The budget the app gives itself.</summary>
    internal const int HostShutdownSeconds = DockerStopSeconds - HostReserveSeconds;

    /// <inheritdoc/>
    public void Configure(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ShutdownTimeout = TimeSpan.FromSeconds(HostShutdownSeconds);
        options.ServicesStopConcurrently = true;
    }
}
