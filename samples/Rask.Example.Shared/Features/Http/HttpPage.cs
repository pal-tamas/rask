using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("http")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class HttpPage : Component
{
    protected override RenderResult Head => Title()["HttpClient + DI — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "HttpClient + DI",
            "HttpClient is registered as a service in Program.cs and injected into pages through their primary constructor. This demo fetches data/posts-1.json — a static JSON file the app serves from its own origin, so the showcase stays self-contained and offline-safe."),
        H2(Class: "h4 mt-4 mb-3")["Register"],
        CodeSample(
            EmbeddedSource.Read("HttpRegisterDemo.cs"),
            Notes:
            "Relative URLs require BaseAddress. WasmHostBuilder.BaseAddress is the page origin (and carries any sub-path) — read it lazily inside the factory so it fires after the JS module imports.",
            Result: HttpRegisterDemo()),
        H2(Class: "h4 mt-5 mb-3")["Inject and fetch"],
        CodeSample(
            EmbeddedSource.Read("HttpFetchDemo.cs"),
            Notes:
            "OnMountAsync runs once on first render. The framework's async lifecycle handler triggers a re-render when the awaited task completes. Component.CancellationToken cancels on unmount — navigate away mid-fetch and the in-flight request aborts.",
            Result: HttpFetchDemo()),
        Div(Class: "alert alert-info d-flex align-items-start mt-4")[
            I(Class: "bi bi-info-circle-fill me-3 fs-4"),
            Div()[
                Strong()["Same demo, two hosts."],
                " Under ", Code()["Rask.Example.Server"],
                " the request is a loopback call to the server's own static file. Under ",
                Code()["Rask.Example.Wasm"],
                " (and the GitHub Pages deploy) the browser fetches the same file from the AppBundle. ",
                "The page code is identical — only the BaseAddress differs per host."
            ]
        ]
    ];
}
