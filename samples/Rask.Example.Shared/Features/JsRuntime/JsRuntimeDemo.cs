using Microsoft.JSInterop;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Round-trips <see cref="IJSRuntime" /> against <c>sessionStorage</c>. Works on both Server
///     (per-session WS-bound <c>RaskJSRuntime</c>) and WASM (in-process bridge via <c>JSImport</c>) —
///     the unified IJSRuntime surface keeps the component identical across hosts. IJSRuntime is
///     injected through the ctor (the framework's DI seam), mirroring <c>ElementRefDemo</c>.
/// </summary>
public sealed class JsRuntimeDemo(IJSRuntime js) : Component
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

    protected override RenderResult Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Div(Class: "mb-3")[
                    Label(Class: "form-label", For: "demo-input")["sessionStorage value"],
                    Input(
                        Id: "demo-input",
                        Class: "form-control",
                        Value: _input,
                        OnInput: v => _input = v)
                ],
                Div(Class: "d-flex gap-2 flex-wrap mb-3")[
                    Button(Class: "btn btn-primary btn-sm", Id: "demo-set", OnClickAsync: SetAsync)[
                        I(Class: "bi bi-save me-1"), "Set"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "demo-read", OnClickAsync: ReadAsync)[
                        I(Class: "bi bi-arrow-clockwise me-1"), "Read"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "demo-remove", OnClickAsync: RemoveAsync)[
                        I(Class: "bi bi-trash me-1"), "Remove"]
                ],
                Div(Class: "mb-2")[
                    Span(Class: "text-secondary small text-uppercase")["Last read"],
                    Div()[Code(Class: "fs-6", Id: "demo-last-read")[_lastRead ?? "(null)"]]
                ],
                Div()[
                    Span(Class: "text-secondary small text-uppercase")["Status"],
                    Div()[Code(Class: "fs-6", Id: "demo-status")[_status ?? "(idle)"]]
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
