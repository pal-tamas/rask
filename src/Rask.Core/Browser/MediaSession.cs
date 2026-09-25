using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
