namespace Rask.Core.Browser;

/// <summary>An open broadcast-channel connection. Dispose to close it.</summary>
public interface IBroadcastChannelConnection : IAsyncDisposable
{
    /// <summary>
    ///     Posts <paramref name="message" /> to all <em>other</em> connections of this channel's name
    ///     (<c>BroadcastChannel.postMessage</c>). This connection does not receive its own message.
    /// </summary>
    ValueTask PostAsync(string message);
}
