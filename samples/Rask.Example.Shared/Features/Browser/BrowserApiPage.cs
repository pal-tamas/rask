using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Hosts <see cref="BrowserApiDemo" />: the typed browser-API foundation (Storage, Clipboard,
///     Geolocation, Navigator) injected as services and identical on Server and WASM. The first step
///     toward PWA support — a discoverable, testable C# surface over the Web APIs that previously
///     required raw <c>IJSRuntime</c> string identifiers.
/// </summary>
[Route("browser")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BrowserApiPage : Component
{
    protected override RenderResult Head => Title()["Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Typed browser APIs",
            "Strongly-typed C# wrappers over the Web APIs — injected as services, identical on Server and WASM."),
        P(Class: "text-secondary")[
            "Instead of raw ", Code()["IJSRuntime.InvokeAsync(\"localStorage.getItem\", …)"],
            " calls, inject ", Code()["IBrowserStorage"], ", ", Code()["IClipboard"], ", ",
            Code()["IGeolocation"], ", or ", Code()["INavigatorInfo"],
            " through a component constructor and await typed methods. Clipboard and geolocation are ",
            "browser-gated (secure context + permission), so each call is wrapped in try/catch."
        ],
        CodeSample(
            ["BrowserApiDemo.cs"],
            Notes:
            "Each wrapper is a thin, awaitable layer over the unified IJSRuntime. Storage and clipboard " +
            "methods are plain function calls; navigator.onLine/language are property reads the client " +
            "returns directly; getCurrentPosition is callback-based, so it goes through the framework's " +
            "__raskApi.geolocation helper (shared by both transports via rask-api.js).",
            Result: BrowserApiDemo()),
        Div(Class: "alert alert-info d-flex align-items-start")[
            I(Class: "bi bi-info-circle-fill me-3 fs-4"),
            Div()[
                Strong()["First step toward PWAs:"],
                " these are the Web APIs that work the same on both transports. WASM-only PWA APIs ",
                "(service worker, cache, manifest, offline) build on the same wrapper pattern in a later step."
            ]
        ]
    ];
}
