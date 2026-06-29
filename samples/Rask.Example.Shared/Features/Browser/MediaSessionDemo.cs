using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IMediaSession" /> — publish now-playing metadata to the OS (lock screen / media hub) and
///     handle hardware media keys. Publish the metadata, then press a media key (or use the lock-screen
///     controls): the browser pushes the action to C#, which appends it to the log (the handler calls
///     <c>StateHasChanged()</c>, the sanctioned pattern for an externally-pushed update). Honored fully only
///     while media is actually playing.
/// </summary>
public sealed class MediaSessionDemo(IMediaSession media) : Component, IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _handlers = [];
    private string _status = "(idle)";
    private string _last = "(none yet)";

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender || _handlers.Count > 0)
        {
            return;
        }

        if (!await media.IsSupportedAsync())
        {
            _status = "Media Session not supported";
            StateHasChanged();
            return;
        }

        foreach (var action in new[]
        {
            MediaSessionAction.Play, MediaSessionAction.Pause,
            MediaSessionAction.PreviousTrack, MediaSessionAction.NextTrack
        })
        {
            var captured = action;
            try
            {
                _handlers.Add(await media.SetActionHandlerAsync(captured, () =>
                {
                    _last = captured.ToString();
                    StateHasChanged();
                    return Task.CompletedTask;
                }));
            }
            catch
            {
                // Browser doesn't support this particular action — skip it.
            }
        }
    }

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex flex-wrap gap-2 mb-3")[
                    Button(Class: "btn btn-sm btn-primary", Id: "ms-publish", OnClickAsync: Publish)["Publish metadata"],
                    Button(Class: "btn btn-sm btn-outline-primary", Id: "ms-playing",
                        OnClickAsync: () => SetState(PlaybackState.Playing, "playing"))["Mark playing"],
                    Button(Class: "btn btn-sm btn-outline-primary", Id: "ms-paused",
                        OnClickAsync: () => SetState(PlaybackState.Paused, "paused"))["Mark paused"],
                    Button(Class: "btn btn-sm btn-outline-danger", Id: "ms-clear", OnClickAsync: Clear)["Clear"]
                ],
                P(Class: "small text-secondary mb-2")[
                    "After publishing, use your keyboard's media keys (or the OS media controls) — the action "
                    + "shows below. Lock-screen integration activates fully while audio is playing."],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "ms-status")[_status]],
                Div(Class: "small text-secondary")["Last action: ", Code(Id: "ms-last")[_last]]
            ]
        ];

    private async Task Publish()
    {
        try
        {
            await media.SetMetadataAsync(new MediaMetadata
            {
                Title = "Rask Showcase Track",
                Artist = "Rask",
                Album = "Browser APIs",
                Artwork = [new MediaArtwork("icon.svg", "any", "image/svg+xml")]
            });
            _status = "metadata published";
        }
        catch (Exception ex)
        {
            _status = "publish failed: " + ex.Message;
        }
    }

    private async Task SetState(PlaybackState state, string label)
    {
        try
        {
            await media.SetPlaybackStateAsync(state);
            _status = $"playback state: {label}";
        }
        catch (Exception ex)
        {
            _status = "set state failed: " + ex.Message;
        }
    }

    private async Task Clear()
    {
        try
        {
            await media.ClearAsync();
            _status = "cleared";
            _last = "(none yet)";
        }
        catch (Exception ex)
        {
            _status = "clear failed: " + ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var handler in _handlers)
        {
            await handler.DisposeAsync();
        }
    }
}
