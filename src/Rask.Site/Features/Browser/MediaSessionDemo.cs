
namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>MediaSession</c> from Rask.Web — publish now-playing metadata to the OS (lock screen / media hub) and
///     handle hardware media keys. Publish the metadata, then press a media key (or use the lock-screen controls): the
///     browser runs the C# action handler, which shows the action below. Honored fully only while media is actually
///     playing.
/// </summary>
public sealed partial class MediaSessionDemo : Component
{
    private static readonly Types.MediaSessionAction[] Actions =
    [
        Types.MediaSessionAction.Play, Types.MediaSessionAction.Pause,
        Types.MediaSessionAction.Previoustrack, Types.MediaSessionAction.Nexttrack
    ];

    private readonly List<Types.MediaSessionAction> _handled = [];
    private string _status = "(idle)";
    private string _last = "(none yet)";

    protected override async Task OnFirstRender()
    {
        if (!await Navigator.MediaSession.IsSupported)
        {
            _status = "Media Session not supported";
            return;
        }

        foreach (var action in Actions.Except(_handled))
        {
            try
            {
                await Navigator.MediaSession.SetActionHandler(action, details => _last = details.Action.ToString());
                _handled.Add(action);
            }
            catch (Microsoft.JSInterop.JSException)
            {
                // The browser does not support this particular action: setActionHandler throws, so skip it.
            }
        }
    }

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                    Ui.Button.Primary.Id("ms-publish").OnClick(Publish)["Publish metadata"],
                    Ui.Button.Primary.Outline
                        .Id("ms-playing")
                        .OnClick(() => SetState(Types.MediaSessionPlaybackState.Playing, "playing"))["Mark playing"],
                    Ui.Button.Primary.Outline
                        .Id("ms-paused")
                        .OnClick(() => SetState(Types.MediaSessionPlaybackState.Paused, "paused"))["Mark paused"],
                    Ui.Button.Error.Outline.Id("ms-clear").OnClick(Clear)["Clear"]
                ],
                P.Class("text-sm text-ui-muted mb-2")[
                    "After publishing, use your keyboard's media keys (or the OS media controls) — the action "
                    + "shows below. Lock-screen integration activates fully while audio is playing."],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("ms-status")[_status]],
                Div.Class("text-sm text-ui-muted")["Last action: ", Code.Id("ms-last")[_last]]
            ];

    // navigator.mediaSession.metadata = new MediaMetadata({ … }): the new object is kept, set by its handle, then let go.
    private async Task Publish()
    {
        try
        {
            await using var metadata = await Rask.Web.MediaMetadata.Create(new()
            {
                Title = "Rask Showcase Track",
                Artist = "Rask",
                Album = "Browser APIs",
                Artwork = [new() { Src = "icon.svg", Sizes = "any", Type = "image/svg+xml" }]
            });
            await Navigator.MediaSession.SetMetadata(metadata);
            _status = "metadata published";
        }
        catch (Exception ex)
        {
            _status = "publish failed: " + ex.Message;
        }
    }

    private async Task SetState(Types.MediaSessionPlaybackState state, string label)
    {
        try
        {
            await Navigator.MediaSession.SetPlaybackState(state);
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
            await Navigator.MediaSession.SetMetadata(null);
            await Navigator.MediaSession.SetPlaybackState(Types.MediaSessionPlaybackState.None);
            _status = "cleared";
            _last = "(none yet)";
        }
        catch (Exception ex)
        {
            _status = "clear failed: " + ex.Message;
        }
    }

    // setActionHandler(action, null) takes each handler back off the OS controls.
    protected override async Task OnUnmount()
    {
        foreach (var action in _handled)
        {
            await Navigator.MediaSession.SetActionHandler(action, (Action<Types.MediaSessionActionDetails>?)null);
        }
    }
}
