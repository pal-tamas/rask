using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Hosts <see cref="JsRuntimeDemo" />: a <c>sessionStorage</c> round-trip through the unified
///     <c>IJSRuntime</c> surface, identical on Server (per-session WS-bound <c>RaskJSRuntime</c>) and
///     WASM (in-process bridge via <c>JSImport</c>). The page shows the demo's real source beside the
///     live result via <see cref="CodeSample" />.
/// </summary>
[Route("jsruntime")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class JsRuntimePage : Component
{
    protected override RenderResult Head => Title()["IJSRuntime — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "IJSRuntime — sessionStorage round-trip",
            "Call into window APIs from C# and await the result, identically on Server and WASM."),
        P(Class: "text-secondary")[
            "Type a value, click ", Strong()["Set"], " to write it to ",
            Code()["sessionStorage"], " via ", Code()["IJSRuntime.InvokeVoidAsync"],
            ". Click ", Strong()["Read"], " to read it back. Refresh the page — ",
            Code()["OnRendered"], " reads the saved value automatically on the next mount."
        ],
        CodeSample(
            ["JsRuntimeDemo.cs"],
            Notes:
            "Every call goes through the unified IJSRuntime: window APIs are resolved by their dotted " +
            "identifier (e.g. sessionStorage.getItem), invoked, and the result is shipped back to the " +
            "awaiting ValueTask<T>. OnRenderedAsync seeds the field from storage on first mount.",
            Result: JsRuntimeDemo()),
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
}
