namespace Rask.Core.Browser;

/// <summary>What the relay told us. Every event carries the peer it concerns, where there is one.</summary>
public sealed record SignalingHandlers
{
    /// <summary>
    ///     We joined. Carries our own peer id and the peers already in the room — the ones we should offer
    ///     to, since a peer that arrives later will offer to us instead. That asymmetry is what stops both
    ///     sides offering at once (an SDP "glare" collision neither browser resolves for you).
    /// </summary>
    public Func<string, IReadOnlyList<string>, Task>? OnJoined { get; init; }

    /// <summary>Another peer joined. Expect an offer from them; don't send one.</summary>
    public Func<string, Task>? OnPeerJoined { get; init; }

    /// <summary>A peer left. Dispose the connection you held for it.</summary>
    public Func<string, Task>? OnPeerLeft { get; init; }

    /// <summary>
    ///     A peer sent us a payload — the string they passed to <see cref="ISignalingConnection.Send" />,
    ///     verbatim. Nothing between the two browsers parsed it.
    /// </summary>
    public Func<string, string, Task>? OnSignal { get; init; }

    /// <summary>The relay refused something, with its reason. The socket stays open.</summary>
    public Func<string, Task>? OnError { get; init; }

    /// <summary>The socket closed — by us, by the relay, or by the network.</summary>
    public Func<Task>? OnClosed { get; init; }
}
