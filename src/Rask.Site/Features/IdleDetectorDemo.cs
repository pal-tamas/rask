
namespace Rask.Site.Features;

/// <summary>
///     <c>IdleDetector</c> (Rask.Web) — request the <c>idle-detection</c> permission from a gesture, then
///     watch for the user going idle or the screen locking. WASM-only: permission needs a live gesture and
///     the detector needs the live document.
/// </summary>
public sealed partial class IdleDetectorDemo : Component
{
    private Types.IdleDetector? _idle;
    private IAsyncDisposable? _watch;
    private string _user = "active";
    private string _screen = "unlocked";
    private string _status = "(idle)";

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Ui.Button.Primary.Class("mb-3").Id("idle-start").OnClick(Start)["Start watching (60s threshold)"],
                Div.Class("text-sm text-ui-muted")["User: ", Code.Id("idle-user")[_user]],
                Div.Class("text-sm text-ui-muted")["Screen: ", Code.Id("idle-screen")[_screen]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("idle-status")[_status]]
            ];

    private async Task Start()
    {
        if (_idle is not null)
        {
            return;
        }

        try
        {
            if (await IdleDetector.RequestPermission() is not Types.PermissionState.Granted)
            {
                _status = "Permission denied";
                return;
            }

            // The detector's `change` event: read both states each time either one moves.
            var idle = await IdleDetector.Create();
            _idle = idle;
            _watch = await idle.OnChange(async () =>
            {
                _user = await idle.UserState is Types.UserIdleState.Idle ? "idle" : "active";
                _screen = await idle.ScreenState is Types.ScreenIdleState.Locked ? "locked" : "unlocked";
            });
            await idle.Start(new() { Threshold = 60_000 });
            _status = "Watching — stop interacting for 60s to go idle";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    protected override async Task OnUnmount()
    {
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
        }

        if (_idle is not null)
        {
            await _idle.DisposeAsync();
        }
    }
}
