namespace Rask.Core.Browser;

/// <summary>One data channel on a peer connection. Dispose to close it.</summary>
public interface IRtcDataChannel : IAsyncDisposable
{
    /// <summary>The channel's label, as both peers see it.</summary>
    string Label { get; }

    /// <summary>
    ///     Starts delivering messages to <paramref name="onMessages" />. Messages the peer sent before this
    ///     call are buffered by the framework and ride the first batch, so a channel opened by the remote
    ///     peer loses nothing between arriving and being listened to. Calling it again replaces the handler.
    /// </summary>
    ValueTask Listen(Func<IReadOnlyList<RtcMessage>, Task> onMessages);

    /// <summary>Sends a string to the other peer.</summary>
    ValueTask Send(string text);

    /// <summary>Sends bytes to the other peer.</summary>
    ValueTask Send(byte[] data);
}
