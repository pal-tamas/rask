using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>MDN's Fullscreen API from Rask.Web — present one element ref fullscreen, then exit (orientation locking needs it first).</summary>
public sealed partial class FullscreenDemo : Component
{
    private readonly ElementRef<HTMLDivElement> _demo = new();
    private readonly ElementRef<HTMLDivElement> _stage = new();
    private string? _status;

    protected override Component? Render() =>
        Div.Ref(_demo).Class("bg-ui-bg")[
            Ui.Card[
                Div
                    .Ref(_stage)
                    .Class("border rounded bg-ui-well flex items-center justify-center mb-2")
                    .Style("min-height: 8rem")[
                    Span.Class("text-ui-muted text-sm")["This box goes fullscreen."]
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Primary.Id("fullscreen-enter").OnClick(() => Enter(_stage, "Box"))["Fullscreen this box"],
                    Ui.Button
                        .Id("fullscreen-page")
                        .OnClick(() => Enter(_demo, "Demo"))["Fullscreen the whole demo"],
                    Ui.Button.Red.Id("fullscreen-exit").OnClick(Exit)["Exit"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("fullscreen-status")[_status ?? "(idle)"]]
            ]
        ];

    // requestFullscreen() rejects without a user gesture, so it runs in the click.
    private async Task Enter(ElementRef<HTMLDivElement> target, string what)
    {
        try
        {
            if (!await Document.FullscreenEnabled)
            {
                _status = "Fullscreen not available in this browser";
                return;
            }

            await target.RequestFullscreen();
            _status = await Document.FullscreenElement == target
                ? what + " is fullscreen — press Esc or Exit to leave"
                : "Fullscreen request did not take";
        }
        catch (JSException ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    // exitFullscreen() rejects when nothing is fullscreen, so ask first.
    private async Task Exit()
    {
        try
        {
            if (await Document.FullscreenElement is not null)
            {
                await Document.ExitFullscreen();
            }

            _status = await Document.FullscreenElement is null ? "Exited fullscreen" : "Still fullscreen";
        }
        catch (JSException ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }
}
