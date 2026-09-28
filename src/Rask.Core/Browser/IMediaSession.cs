namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Media Session API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/MediaSession" />) — publish now-playing
///     metadata to the OS (lock screen, media hub) and handle hardware media keys / lock-screen controls,
///     so your in-page audio or video feels like a native player. Works on <b>both transports</b>; inject
///     it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         Metadata and playback state are one-shot setters. Action handlers are <i>subscriptions</i>: the
///         browser <b>pushes</b> each press to the C# callback (via a static <c>[JSInvokable]</c>, so one
///         wiring serves both transports). Register from a lifecycle hook and dispose the returned handle on
///         unmount. A handler that updates state should call <c>StateHasChanged()</c> (it's a subscription,
///         not a render/binding callback, so RASK026 doesn't apply).
///     </para>
///     <para>
///         The session is only honored while media is actually playing in the page; pair this with an
///         <c>&lt;audio&gt;</c>/<c>&lt;video&gt;</c> element. Some browsers reject handlers for unsupported
///         actions — gate on <see cref="IsSupportedAsync" /> and <c>try/catch</c>.
///     </para>
/// </remarks>
public interface IMediaSession
{
    /// <summary>Whether the browser supports the Media Session API (<c>"mediaSession" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Publishes <paramref name="metadata" /> as the now-playing information shown by the OS.</summary>
    ValueTask SetMetadataAsync(MediaMetadata metadata);

    /// <summary>Reports the current <paramref name="state" /> so the OS shows the right play/pause affordance.</summary>
    ValueTask SetPlaybackStateAsync(PlaybackState state);

    /// <summary>
    ///     Registers <paramref name="handler" /> for <paramref name="action" /> (a media key / lock-screen
    ///     control). Dispose the returned handle to remove the handler.
    /// </summary>
    ValueTask<IAsyncDisposable> SetActionHandlerAsync(MediaSessionAction action, Func<Task> handler);

    /// <summary>Clears the now-playing metadata and resets playback state to <see cref="PlaybackState.None" />.</summary>
    ValueTask ClearAsync();
}
