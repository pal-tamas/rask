using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Rask.Hosting.Shared;

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
/// <c>rask deploy</c> names the budget itself: it hands the app <c>ASPNETCORE_SHUTDOWNTIMEOUTSECONDS</c> — its
/// <c>stopTimeout</c> less a margin for what happens after <c>StopAsync</c> returns (container teardown, log
/// flush, DI disposal, EF connection teardown) — and that value is kept. The 15 seconds here are only for a
/// host nobody gave a budget: one deployed some other way, or by an older <c>rask</c> that SIGKILLed at 20s.
/// An app that needs a different one configures <see cref="HostOptions"/> <em>after</em> <c>AddRask</c>:
/// options setups run in registration order, so the last writer decides.
/// </para>
/// </remarks>
internal sealed class RaskShutdownDefaults(IConfiguration? configuration = null) : IConfigureOptions<HostOptions>
{
    /// <summary>The host-configuration key .NET binds <see cref="HostOptions.ShutdownTimeout"/> from.</summary>
    /// <remarks>
    /// <c>rask deploy</c> sets it (as <c>ASPNETCORE_SHUTDOWNTIMEOUTSECONDS</c>) from the app's <c>stopTimeout</c>,
    /// so the budget the app gives itself and the one the deploy waits for come from one number.
    /// </remarks>
    internal const string ShutdownTimeoutKey = "shutdownTimeoutSeconds";

    /// <summary>The stop window assumed when nobody named one: the 20s an older <c>rask deploy</c> allowed.</summary>
    internal const int DockerStopSeconds = 20;

    /// <summary>Margin between the app finishing and the SIGKILL landing.</summary>
    internal const int HostReserveSeconds = 5;

    /// <summary>The budget the app gives itself.</summary>
    internal const int HostShutdownSeconds = DockerStopSeconds - HostReserveSeconds;

    /// <summary>.NET's own default, and the signal that nobody has chosen a budget yet.</summary>
    /// <remarks>
    /// The generic host binds <c>HostOptions</c> from configuration before this runs, which is how
    /// <c>ASPNETCORE_SHUTDOWNTIMEOUTSECONDS</c> and the <c>shutdownTimeoutSeconds</c> key reach it.
    /// Writing unconditionally would silently discard both — an operator who raised the budget for a long
    /// drain would get 15 seconds and no warning — so the timeout is only replaced while it still holds
    /// the framework default.
    /// </remarks>
    private static readonly TimeSpan _frameworkDefault = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public void Configure(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A budget configuration names is a decision even when it equals the framework default — a stopTimeout
        // of 35s hands the app exactly 30 — so the value check alone would overrule it.
        if (options.ShutdownTimeout == _frameworkDefault && string.IsNullOrEmpty(configuration?[ShutdownTimeoutKey]))
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(HostShutdownSeconds);
        }

        // Not conditional, unlike the timeout: this has no configuration binding to defer to, and stopping
        // sequentially is never what an app with Rask's hosted services wants. Configure HostOptions after
        // AddRask to set it back.
        options.ServicesStopConcurrently = true;
    }
}
