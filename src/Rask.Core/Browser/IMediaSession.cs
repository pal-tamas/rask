using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>An artwork image for the media session (an entry of <c>MediaMetadata.artwork</c>).</summary>
/// <param name="Src">Image URL.</param>
/// <param name="Sizes">Space-separated sizes, e.g. <c>"512x512"</c> (optional).</param>
/// <param name="Type">MIME type, e.g. <c>"image/png"</c> (optional).</param>
public sealed record MediaArtwork(
    string Src,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sizes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type = null);

/// <summary>
///     Now-playing metadata shown by the OS (lock screen, media hub, smart-watch) — the
///     <c>MediaMetadata</c> of the <see href="https://developer.mozilla.org/en-US/docs/Web/API/MediaSession">
///     Media Session API</see>.
/// </summary>
public sealed record MediaMetadata
{
    /// <summary>Track / content title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    /// <summary>Artist / author / performer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Artist { get; init; }

    /// <summary>Album / collection name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Album { get; init; }

    /// <summary>Artwork images (the OS picks the best-fitting size).</summary>
    public IReadOnlyList<MediaArtwork> Artwork { get; init; } = [];
}

/// <summary>Playback state reported to the OS (the <c>playbackState</c> of the media session).</summary>
public enum PlaybackState
{
    /// <summary>No active media (<c>"none"</c>).</summary>
    None,

    /// <summary>Media is paused (<c>"paused"</c>).</summary>
    Paused,

    /// <summary>Media is playing (<c>"playing"</c>).</summary>
    Playing
}

/// <summary>
///     A media control the OS can raise (a media key, lock-screen button, or headset gesture) — the
///     actions of the <see href="https://developer.mozilla.org/en-US/docs/Web/API/MediaSession/setActionHandler">
///     Media Session API</see>.
/// </summary>
public enum MediaSessionAction
{
    /// <summary>Resume / start playback.</summary>
    Play,

    /// <summary>Pause playback.</summary>
    Pause,

    /// <summary>Stop playback.</summary>
    Stop,

    /// <summary>Skip to the next track.</summary>
    NextTrack,

    /// <summary>Skip to the previous track.</summary>
    PreviousTrack,

    /// <summary>Seek backward by a short interval.</summary>
    SeekBackward,

    /// <summary>Seek forward by a short interval.</summary>
    SeekForward
}

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

/// <summary>
///     Infrastructure for <see cref="IMediaSession" /> — routes a pushed media action back to the right C#
///     callback by handler id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskMediaSession</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MediaSessionInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<Task>> Handlers = new();

    internal static int Register(Func<Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a registered media action fires; do not call.</summary>
    [JSInvokable("RaskMediaSessionAction")]
    public static Task Invoke(int id) =>
        Handlers.TryGetValue(id, out var handler) ? handler() : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IMediaSession" />, backed by the unified <see cref="IJSRuntime" />. Metadata and
///     state go through the framework's <c>__raskMediaSession</c> helper (building a <c>MediaMetadata</c> is
///     a constructor <see cref="IJSRuntime" /> can't call); each action handler is wired to a static
///     <c>[JSInvokable]</c> by a C#-minted id.
/// </summary>
public sealed class MediaSession : IMediaSession
{
    private readonly IJSRuntime _js;

    // Root MediaSessionInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Invoke method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(MediaSessionInterop))]
    public MediaSession(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskMediaSession.isSupported");

    /// <inheritdoc />
    public ValueTask SetMetadataAsync(MediaMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return _js.InvokeVoidAsync("__raskMediaSession.setMetadata", metadata);
    }

    /// <inheritdoc />
    public ValueTask SetPlaybackStateAsync(PlaybackState state) =>
        _js.InvokeVoidAsync("__raskMediaSession.setPlaybackState", ToToken(state));

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> SetActionHandlerAsync(MediaSessionAction action, Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = MediaSessionInterop.Register(handler);
        try
        {
            await _js.InvokeVoidAsync("__raskMediaSession.setActionHandler", id, ToToken(action));
        }
        catch
        {
            MediaSessionInterop.Unregister(id);
            throw;
        }

        return new ActionHandler(_js, id);
    }

    /// <inheritdoc />
    public ValueTask ClearAsync() => _js.InvokeVoidAsync("__raskMediaSession.clear");

    private static string ToToken(PlaybackState state) => state switch
    {
        PlaybackState.Paused => "paused",
        PlaybackState.Playing => "playing",
        _ => "none"
    };

    private static string ToToken(MediaSessionAction action) => action switch
    {
        MediaSessionAction.Play => "play",
        MediaSessionAction.Pause => "pause",
        MediaSessionAction.Stop => "stop",
        MediaSessionAction.NextTrack => "nexttrack",
        MediaSessionAction.PreviousTrack => "previoustrack",
        MediaSessionAction.SeekBackward => "seekbackward",
        MediaSessionAction.SeekForward => "seekforward",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    private sealed class ActionHandler(IJSRuntime js, int id) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            MediaSessionInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskMediaSession.removeActionHandler", id);
        }
    }
}
