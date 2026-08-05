using Microsoft.Extensions.Hosting;

namespace Rask.Server;

/// <summary>
///     Runs the shutdown drain: tells every connected client to reconnect now, then releases the sockets.
/// </summary>
/// <remarks>
///     <para>
///         A hosted service rather than an <c>ApplicationStopping</c> callback, for one reason that only
///         shows up under load. Those callbacks are synchronous, so driving the sends from one means
///         blocking a thread until they finish — and a host shutting down while serving traffic has a busy
///         thread pool, which is precisely when blocking on work that needs the pool to progress stops
///         progressing. <c>StopAsync</c> is awaited by the host instead, with no thread held hostage.
///     </para>
///     <para>
///         The ordering still works because sockets watch <see cref="LiveSessionStore.Drained" /> and not
///         <c>ApplicationStopping</c>: by the time this runs, shutdown has begun but every socket is still
///         open, which is exactly the window the drain needs.
///     </para>
/// </remarks>
internal sealed class LiveSessionDrainService(LiveSessionStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => store.DrainAsync();
}
