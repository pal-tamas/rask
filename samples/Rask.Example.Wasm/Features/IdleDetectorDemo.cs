using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IIdleDetector" /> — request the <c>idle-detection</c> permission from a gesture, then
///     watch for the user going idle or the screen locking. WASM-only: permission needs a live gesture and
///     the detector needs the live document.
/// </summary>
public sealed class IdleDetectorDemo(IIdleDetector idle) : Component, IAsyncDisposable
{
    private IAsyncDisposable? _watch;
    private string _user = "active";
    private string _screen = "unlocked";
    private string _status = "(idle)";

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Button(Class: "btn btn-primary btn-sm mb-3", Id: "idle-start", OnClickAsync: Start)[
                    "Start watching (60s threshold)"],
                Div(Class: "small text-secondary")["User: ", Code(Id: "idle-user")[_user]],
                Div(Class: "small text-secondary")["Screen: ", Code(Id: "idle-screen")[_screen]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "idle-status")[_status]]
            ]
        ];

    private async Task Start()
    {
        if (_watch is not null)
        {
            return;
        }

        try
        {
            if (!await idle.IsSupportedAsync())
            {
                _status = "Idle Detection not supported in this browser";
                return;
            }

            if (await idle.RequestPermissionAsync() != "granted")
            {
                _status = "Permission denied";
                return;
            }

            _watch = await idle.WatchAsync(reading =>
            {
                _user = reading.UserIdle ? "idle" : "active";
                _screen = reading.ScreenLocked ? "locked" : "unlocked";
                StateHasChanged();
                return Task.CompletedTask;
            });
            _status = "Watching — stop interacting for 60s to go idle";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
        }
    }
}
