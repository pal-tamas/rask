using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>MDN's Picture-in-Picture API from Rask.Web — float a canvas-fed <c>&lt;video&gt;</c> (sibling scoped TS) into a miniplayer.</summary>
public sealed partial class PictureInPictureDemo : Component
{
    private readonly ElementRef<HTMLVideoElement> _video = new();
    private Types.PictureInPictureWindow? _window;
    private string _status = "(idle)";

    protected override async Task OnFirstRender()
    {
        try
        {
            await Start(_video);
            _status = await Document.PictureInPictureEnabled
                ? "Playing — click \"Open miniplayer\""
                : "Picture-in-Picture not supported in this browser";
        }
        catch (JSException ex)
        {
            _status = "Setup failed: " + ex.Message;
        }

        StateHasChanged();
    }

    protected override Component? Render() =>
        Ui.Card[
                Video
                    .Ref(_video)
                    .Width(320)
                    .Height(180)
                    .Muted()
                    .PlaysInline()
                    .Controls()
                    .Class("rounded border mb-2 bg-slate-900"),
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Primary.Id("pip-enter").OnClick(Enter)["Open miniplayer"],
                    Ui.Button.Red.Id("pip-exit").OnClick(Exit)["Exit"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("pip-status")[_status]]
            ];

    // requestPictureInPicture() rejects without a user gesture, so it runs in the click.
    private async Task Enter()
    {
        try
        {
            await Release();
            _window = await _video.RequestPictureInPicture();
            _status = $"In a {await _window.Width}×{await _window.Height} miniplayer — drag it anywhere, then Exit to bring it back";
        }
        catch (JSException ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    // exitPictureInPicture() rejects when nothing is in the miniplayer, so ask first.
    private async Task Exit()
    {
        try
        {
            if (await Document.PictureInPictureElement == _video)
            {
                await Document.ExitPictureInPicture();
            }

            await Release();
            _status = await Document.PictureInPictureElement == _video ? "Still in miniplayer" : "Back in the page";
        }
        catch (JSException ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    protected override Task OnUnmount() => Release();

    private async Task Release()
    {
        if (_window is not null)
        {
            await _window.DisposeAsync();
            _window = null;
        }
    }
}
