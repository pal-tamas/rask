using Microsoft.JSInterop;
using Rask.Core.Routing;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

/// <summary>
///     Round-trip <see cref="IJSRuntime" /> against <c>sessionStorage</c>. Works
///     on both Server (per-session WS-bound <c>RaskJSRuntime</c>) and WASM (in-
///     process bridge via <c>JSImport</c>) — the unified IJSRuntime surface
///     keeps the component identical across hosts.
/// </summary>
[Route("jsruntime")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class JsRuntimePage(IJSRuntime js) : Component
{
    private string _input = string.Empty;
    private string? _lastRead;
    private string? _status;

    protected override RenderResult Head => Title()["IJSRuntime — Rask"];

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
        [
            H1(Class: "h3 mt-2 mb-3")["IJSRuntime — sessionStorage round-trip"],
            P(Class: "text-secondary")[
                "Type a value, click ", Strong()["Set"], " to write it to ",
                Code()["sessionStorage"], " via ", Code()["IJSRuntime.InvokeVoidAsync"],
                ". Click ", Strong()["Read"], " to read it back. Refresh the page — ",
                Code()["OnRendered"], " reads the saved value automatically on the next mount."
            ],
            Div(Class: "card shadow-sm border-0 mb-4")[
                Div(Class: "card-body")[
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
            ],
            Div(Class: "alert alert-info d-flex align-items-start")[
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div()[
                    Strong()["What's happening:"],
                    " On Server each call queues a global-JS invoke onto the next outbound WS frame; on WASM the call goes through the in-process JS bridge. ",
                    "Either runtime resolves the dotted identifier on ", Code()["window"], " (e.g. ",
                    Code()["sessionStorage.getItem"], "), invokes it, then ships the result back ",
                    "to the awaiting ", Code()["ValueTask<T>"], "."
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
