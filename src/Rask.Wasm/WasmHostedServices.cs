using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core.Diagnostics;

namespace Rask.Wasm;

/// <summary>
///     Runs the app's registered <see cref="IHostedService" />s on the browser host.
/// </summary>
/// <remarks>
///     <para>
///         <c>AddHostedService&lt;T&gt;()</c> already compiles here —
///         <c>Microsoft.Extensions.Hosting.Abstractions</c> is pure abstractions with no reflection — but
///         nothing ever <em>started</em> what it registered, because a WASM app has no generic host. So a
///         background service that works on the server registered fine, resolved fine, and silently never
///         ran. This closes that gap: the same <c>AddHostedService</c> line now means the same thing on
///         both hosts.
///     </para>
///     <para>
///         Failures are reported and swallowed. On the server a hosted service that throws from
///         <c>StartAsync</c> aborts startup, which is right when the process can be restarted by an
///         orchestrator; in a browser tab there is nothing to restart, and refusing to paint the app
///         because a background worker failed would turn a degraded feature into a blank page.
///     </para>
/// </remarks>
internal sealed class WasmHostedServices(IServiceProvider provider)
{
    // The services that actually started, in start order — not everything registered. Stopping one that
    // never started would hand a BackgroundService a stop signal for an ExecuteAsync it never entered.
    private readonly List<IHostedService> _started = [];
    private bool _stopped;

    /// <summary>The services that started, in start order. Test seam.</summary>
    public IReadOnlyList<IHostedService> Started => _started;

    /// <summary>
    ///     Starts every registered hosted service, in registration order.
    /// </summary>
    /// <remarks>
    ///     Sequential, like the generic host: registration order is start order, which is the only
    ///     ordering signal an app has. Note that for a <see cref="BackgroundService" /> "started" means
    ///     <c>ExecuteAsync</c> reached its first await, not that it finished any initialisation — a
    ///     service that must not run before another is *ready* still needs to wait on something explicit.
    ///     That is also why this costs no paint latency.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        IHostedService[] services;
        try
        {
            // Guarded separately, and eagerly materialised so the failure lands HERE. Enumerating this
            // constructs every hosted service, so a throwing constructor — or an unregistered dependency,
            // which is the commonest startup failure of all — faults at this call rather than inside the
            // loop below. Left inside the loop's try it would escape StartAsync entirely, out through
            // RunAsync, and blank the app: exactly what the per-service catch exists to prevent.
            //
            // All-or-nothing by necessity: the container builds the whole set in one call, so there is no
            // seam to isolate one bad constructor from the rest. Reported plainly for that reason.
            services = [.. provider.GetServices<IHostedService>()];
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error,
                "Rask.Wasm",
                "[Rask.Wasm] hosted services could not be constructed (a constructor threw, or a dependency "
                + "is not registered); the app continues without any of them",
                ex);
            return;
        }

        foreach (var service in services)
        {
            try
            {
                await service.StartAsync(cancellationToken).ConfigureAwait(false);
                _started.Add(service);
                ObserveExecution(service);
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.Wasm",
                    $"[Rask.Wasm] hosted service '{service.GetType().Name}' failed to start; the app continues without it",
                    ex);
            }
        }
    }

    /// <summary>
    ///     Reports a <see cref="BackgroundService" /> whose <c>ExecuteAsync</c> faults after it started.
    /// </summary>
    /// <remarks>
    ///     Without this the fault is never observed by anyone: <c>StartAsync</c> has already returned by
    ///     the time <c>ExecuteAsync</c> fails, and nothing on this host awaits the execute task. The
    ///     symptom would be the very one this class exists to remove — a background service that
    ///     registered fine, resolved fine, and silently is not running — only now appearing after the
    ///     first await instead of before it. The generic host has
    ///     <c>BackgroundServiceExceptionBehavior</c> for the same reason; a browser tab has no host to
    ///     stop, so reporting it is all that is available and all that is wanted.
    /// </remarks>
    private static void ObserveExecution(IHostedService service)
    {
        if (service is not BackgroundService { ExecuteTask: { } executing })
        {
            return;
        }

        _ = executing.ContinueWith(
            static (task, state) => RaskDiagnostics.Report(
                RaskLogLevel.Error,
                "Rask.Wasm",
                $"[Rask.Wasm] hosted service '{state}' stopped: its background loop faulted",
                task.Exception),
            service.GetType().Name,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    ///     Stops the started services in reverse start order, giving them <paramref name="grace" /> to finish.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Best-effort by construction. This is driven from <c>pagehide</c>, and the browser does not
    ///         await anything a <c>pagehide</c> handler starts — so a service gets whatever time the browser
    ///         happens to allow, which may be none. It is worth doing anyway: <c>Rask.Jobs</c>' processor
    ///         hands its lease back (and undoes the attempt its claim counted) in <c>StopAsync</c>, so when
    ///         this does land, a closed tab does not park its claimed batch for a whole lease duration.
    ///         When it does not land, the lease expiring is the backstop — exactly as it is for a server
    ///         that was killed rather than drained.
    ///     </para>
    ///     <para>
    ///         Reverse order mirrors the generic host, so a service that depends on an earlier-registered
    ///         one still has it while stopping.
    ///     </para>
    /// </remarks>
    public async Task StopAsync(TimeSpan grace)
    {
        // pagehide can fire more than once in a tab's life (a bfcache round-trip, then a real teardown),
        // and a second stop would hand an already-stopped BackgroundService a second signal.
        if (_stopped)
        {
            return;
        }

        _stopped = true;

        using var deadline = new CancellationTokenSource(grace);

        for (var i = _started.Count - 1; i >= 0; i--)
        {
            var service = _started[i];
            try
            {
                await service.StopAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Warning,
                    "Rask.Wasm",
                    $"[Rask.Wasm] hosted service '{service.GetType().Name}' failed to stop cleanly",
                    ex);
            }
        }
    }
}
