namespace Rask.Example.Shared.Features;

// Illustrates the HttpClient registration pattern shown beside the live result. In each host's
// Program.cs the HttpClient is registered as a singleton pointed at the app's OWN origin, so
// relative fetches (e.g. "data/posts-1.json") resolve to the static files the app serves itself
// and the showcase stays self-contained and offline-safe. The base address differs per host —
// on WASM it is WasmHostBuilder.BaseAddress (the page origin, carrying any sub-path), read
// lazily inside the factory so it fires after the JS module imports; on the Server host it is
// the server's own origin. This component builds the same configured client and shows it.
public sealed class HttpRegisterDemo : Component
{
    // The factory the host registers: configure HttpClient to resolve relative URLs against the
    // app's own origin. Pass the origin in lazily (on WASM: () => WasmHostBuilder.BaseAddress).
    private static HttpClient CreateClient(Func<string> baseAddress) =>
        new() { BaseAddress = new Uri(baseAddress()) };

    protected override Component? Render() =>
        BsCard(Class: "border-0 bg-light")[
            BsCardBody()[
                Div(Class: "small text-secondary text-uppercase mb-1")["Configured HttpClient"],
                P(Class: "mb-0 small")[
                    "BaseAddress: ", Code()[CreateClient(() => "https://localhost/").BaseAddress!.ToString()],
                    " — relative fetches resolve against the app's own origin."
                ]
            ]
        ];
}
