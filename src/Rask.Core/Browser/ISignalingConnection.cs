namespace Rask.Core.Browser;

/// <summary>An open signaling connection. Dispose to leave the room and close the socket.</summary>
public interface ISignalingConnection : IAsyncDisposable
{
    /// <summary>
    ///     Sends <paramref name="payload" /> to one peer in our room. The relay refuses a peer that isn't in
    ///     it, and never delivers a message back to its sender.
    /// </summary>
    ValueTask Send(string toPeerId, string payload);
}
