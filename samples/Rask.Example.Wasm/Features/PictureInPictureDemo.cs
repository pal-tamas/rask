using Microsoft.JSInterop;
using Rask.Core;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IPictureInPicture" /> — float a <c>&lt;video&gt;</c> into an always-on-top miniplayer.
///     The video is synthesized from an animated canvas by the sibling scoped JS
///     (<c>PictureInPictureDemo.js</c>) so the demo needs no shipped video file.
/// </summary>
public sealed partial class PictureInPictureDemo(IPictureInPicture pip, IJSRuntime js) : Component
{
    private readonly ElementRef _video = ElementRef.New();
    private string _status = "(idle)";

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("Rask.PictureInPictureDemo.start", _video);
            _status = await pip.IsSupportedAsync()
                ? "Playing — click \"Open miniplayer\""
                : "Picture-in-Picture not supported in this browser";
        }
        catch (Exception ex)
        {
            _status = "Setup failed: " + ex.Message;
        }

        StateHasChanged();
    }

    protected override Component? Render() =>
        Div.Class("card shadow-sm border-0")[
            Div.Class("card-body")[
                Video
                    .Ref(_video)
                    .Width(320)
                    .Height(180)
                    .Muted(true)
                    .PlaysInline(true)
                    .Controls(true)
                    .Class("rounded border mb-2 bg-dark"),
                Div.Class("d-flex gap-2 flex-wrap mb-2")[
                    Button.Class("btn btn-primary btn-sm").Id("pip-enter").OnClickAsync(Enter)[
                        "Open miniplayer"],
                    Button.Class("btn btn-outline-danger btn-sm").Id("pip-exit").OnClickAsync(Exit)["Exit"]
                ],
                Div.Class("small text-secondary")["Status: ", Code.Id("pip-status")[_status]]
            ]
        ];

    private async Task Enter()
    {
        try
        {
            await pip.RequestAsync(_video);
            _status = "In the miniplayer — drag it anywhere, then Exit to bring it back";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Exit()
    {
        await pip.ExitAsync();
        _status = await pip.IsActiveAsync() ? "Still in miniplayer" : "Back in the page";
    }
}
