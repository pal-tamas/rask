using Rask.Core.Live;
using Rask.Cqrs;

namespace Rask.Querying;

/// <summary>
///     The token a sent command runs under — the host's limit on the event that sent it (its <c>HandlerTimeout</c>)
///     and the host stopping — and never the caller's: a page that leaves drops the answer, not the write half-way.
/// </summary>
internal readonly struct CommandToken : IDisposable
{
    private readonly CancellationTokenSource? _linked;

    private CommandToken(CancellationTokenSource? linked, CancellationToken token)
    {
        Token = token;
        _linked = linked;
    }

    /// <summary>What stops the command.</summary>
    public CancellationToken Token { get; }

    /// <summary>The token for a command sent now, from the event and the app in progress.</summary>
    public static CommandToken Start()
    {
        var stopped = CommandLifetime.Of(Ambient.Services);
        var limit = DispatchEventTokenScope.Host;
        if (!limit.CanBeCanceled)
        {
            return new CommandToken(null, stopped);
        }

        // Only an event with a HandlerTimeout pays for the link.
        var linked = CancellationTokenSource.CreateLinkedTokenSource(limit, stopped);
        return new CommandToken(linked, linked.Token);
    }

    public void Dispose() => _linked?.Dispose();
}
