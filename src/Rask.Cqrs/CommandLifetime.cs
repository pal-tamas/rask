using Microsoft.Extensions.Hosting;

namespace Rask.Cqrs;

/// <summary>
///     What stops a command once it has been sent: the host stopping, never the caller going away. A page that
///     leaves, a tab that closes, a fetch the browser aborts — the command runs to the end it was sent for and only
///     its answer is dropped, as a request the visitor walked away from still finishes on a Laravel server.
/// </summary>
internal static class CommandLifetime
{
    // Cancellable, never cancelled. A token that cannot be cancelled at all would fall through Ambient.Or to the work
    // in progress's — the page's lifetime, the request's abort — the one thing a command must not answer to.
#pragma warning disable S2930 // Lives as long as the process and is never cancelled: there is no point at which to dispose it.
    private static readonly CancellationTokenSource Never = new();
#pragma warning restore S2930

    /// <summary>
    ///     The token a command sent within <paramref name="services" /> runs under: the app's
    ///     <see cref="IHostApplicationLifetime.ApplicationStopped" />, which fires once the host's graceful shutdown has
    ///     waited out what was in flight — or, with no host to stop, one that never fires.
    /// </summary>
    internal static CancellationToken Of(IServiceProvider? services) =>
        services?.GetService(typeof(IHostApplicationLifetime)) is IHostApplicationLifetime host
            ? host.ApplicationStopped
            : Never.Token;
}
