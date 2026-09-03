using Rask.Core;
using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IFullscreen" /> — present a single element fullscreen, then exit. Pass an
///     <see cref="ElementRef" /> to fullscreen just that box (or nothing to fullscreen the whole page).
///     Pairs with <see cref="IScreenOrientation" />: orientation locking needs fullscreen first.
/// </summary>
public sealed partial class FullscreenDemo(IFullscreen fullscreen) : Component
{
    private readonly ElementRef _stage = ElementRef.New();
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div
                    .Ref(_stage)
                    .Class("border rounded bg-ui-well flex items-center justify-center mb-2")
                    .Style("min-height: 8rem")[
                    Span.Class("text-ui-muted text-sm")["This box goes fullscreen."]
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button.Class(Tw.BtnPrimary).Id("fullscreen-enter").OnClickAsync(Enter)[
                        "Fullscreen this box"],
                    Button.Class(Tw.BtnOutlinePrimary).Id("fullscreen-page").OnClickAsync(EnterPage)[
                        "Fullscreen the page"],
                    Button.Class(Tw.BtnOutlineDanger).Id("fullscreen-exit").OnClickAsync(Exit)["Exit"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("fullscreen-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Enter()
    {
        try
        {
            if (!await fullscreen.IsSupportedAsync())
            {
                _status = "Fullscreen not available in this browser";
                return;
            }

            await fullscreen.RequestAsync(_stage);
            _status = "Box is fullscreen — press Esc or Exit to leave";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task EnterPage()
    {
        try
        {
            await fullscreen.RequestAsync();
            _status = "Page is fullscreen";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Exit()
    {
        await fullscreen.ExitAsync();
        _status = await fullscreen.IsActiveAsync() ? "Still fullscreen" : "Exited fullscreen";
    }
}
