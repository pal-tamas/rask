using Microsoft.JSInterop;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Round-trips <see cref="IJSRuntime" /> against <c>sessionStorage</c>. Works on both Server
///     (per-session WS-bound <c>RaskJSRuntime</c>) and WASM (in-process bridge via <c>JSImport</c>) —
///     the unified IJSRuntime surface keeps the component identical across hosts. IJSRuntime is
///     injected through the ctor (the framework's DI seam), mirroring <c>ElementRefDemo</c>.
/// </summary>
public sealed partial class JsRuntimeDemo(IJSRuntime js) : Component
{
    private string _input = string.Empty;
    private string? _lastRead;
    private string? _status;

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _lastRead = await js.InvokeAsync<string?>("sessionStorage.getItem", "rask.jsruntime.demo");
            _status = _lastRead is null ? "(no value yet — try Set)" : $"Read on mount: {_lastRead}";
        }
        catch (Exception ex)
        {
            _status = "Read failed: " + ex.Message;
        }
    }

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("mb-3")[
                    Label.Class(Tw.Label).For("demo-input")["sessionStorage value"],
                    Input
                        .Value(_input)
                        .Id("demo-input")
                        .Class(Tw.Input)
                        .OnInput(v => _input = v)
                ],
                Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                    Button.Type("button").Class(Tw.BtnPrimary).Id("demo-set").OnClickAsync(SetAsync)[
                        UiIcon.Name(UiIconName.Save).Class("me-1"), "Set"],
                    Button.Type("button").Class(Tw.BtnOutlinePrimary)
                        .Id("demo-read")
                        .OnClickAsync(ReadAsync)[
                        UiIcon.Name(UiIconName.Retry).Class("me-1"), "Read"],
                    Button.Type("button").Class(Tw.BtnOutlineDanger)
                        .Id("demo-remove")
                        .OnClickAsync(RemoveAsync)[
                        UiIcon.Name(UiIconName.Trash).Class("me-1"), "Remove"]
                ],
                Div.Class("mb-2")[
                    Span.Class("text-ui-muted text-sm uppercase")["Last read"],
                    Div[Code.Class("text-base").Id("demo-last-read")[_lastRead ?? "(null)"]]
                ],
                Div[
                    Span.Class("text-ui-muted text-sm uppercase")["Status"],
                    Div[Code.Class("text-base").Id("demo-status")[_status ?? "(idle)"]]
                ]
            ]
        ];

    private async Task SetAsync()
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.setItem", "rask.jsruntime.demo", _input);
            _status = $"Set to: {_input}";
        }
        catch (Exception ex)
        {
            _status = "Set failed: " + ex.Message;
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            _lastRead = await js.InvokeAsync<string?>("sessionStorage.getItem", "rask.jsruntime.demo");
            _status = _lastRead is null ? "Read: (null)" : $"Read: {_lastRead}";
        }
        catch (Exception ex)
        {
            _status = "Read failed: " + ex.Message;
        }
    }

    private async Task RemoveAsync()
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", "rask.jsruntime.demo");
            _lastRead = null;
            _status = "Removed";
        }
        catch (Exception ex)
        {
            _status = "Remove failed: " + ex.Message;
        }
    }
}
