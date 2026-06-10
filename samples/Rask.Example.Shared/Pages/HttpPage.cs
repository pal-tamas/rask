using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("http")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class HttpPage(HttpClient http) : Component
{
    private const int MaxTransientRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);

    private string? _error;
    private Post? _post;

    protected override RenderResult Head => Title()["HttpClient + DI — Rask"];

    protected override async Task OnMountAsync()
    {
        for (var attempt = 0;; attempt++)
        {
            try
            {
                _post = await http.GetFromJsonAsync("data/posts-1.json", HttpJsonContext.Default.Post,
                    CancellationToken);
                return;
            }
            // Navigating away unmounts the page and cancels the token — the page is gone, nothing to show.
            catch (OperationCanceledException) { return; }
            // On WASM a hard browser refresh kills the in-flight fetch outside the AbortController, so it
            // surfaces as an HttpRequestException with no StatusCode ("TypeError: Load failed") rather than
            // an OperationCanceledException. The same null-status failure also fires transiently on the
            // freshly-booted page when its first fetch races the discarded page's network teardown — so retry
            // a few times and the page self-heals instead of hanging on the spinner forever.
            catch (HttpRequestException ex) when (ex.StatusCode is null && attempt < MaxTransientRetries)
            {
                try { await Task.Delay(RetryDelay, CancellationToken); }
                catch (OperationCanceledException) { return; }
            }
            // A real HTTP-status failure, or a transport failure that never recovers, surfaces the error banner.
            catch (Exception ex)
            {
                _error = ex.Message;
                return;
            }
        }
    }

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "HttpClient + DI",
            "HttpClient is registered as a service in Program.cs and injected into pages through their primary constructor. This demo fetches data/posts-1.json — a static JSON file the app serves from its own origin, so the showcase stays self-contained and offline-safe."),
        H2(Class: "h4 mt-4 mb-3")["Register"],
        CodeSample(
            """
            // Program.cs — point HttpClient at the app's own origin so relative
            // fetches resolve to the static files it serves itself.
            var host = WasmHostBuilder.CreateDefault();
            host.Services.AddSingleton(_ =>
                new HttpClient {
                    BaseAddress = new Uri(WasmHostBuilder.BaseAddress)
                });
            await host.RunAsync<App>();
            """,
            Notes:
            "Relative URLs require BaseAddress. WasmHostBuilder.BaseAddress is the page origin (and carries any sub-path) — read it lazily inside the factory so it fires after the JS module imports."),
        H2(Class: "h4 mt-5 mb-3")["Inject and fetch"],
        CodeSample(
            """
            [Route("/http")]
            public sealed class HttpPage(HttpClient http) : Component
            {
                private Post? _post;

                protected override async Task OnMountAsync() =>
                    _post = await http.GetFromJsonAsync<Post>("data/posts-1.json", CancellationToken);

                public override RenderResult Render() =>
                    _post is null
                        ? P()[Em()["Loading…"]]
                        : Article()[
                            H3()[_post.Title],
                            P()[_post.Body]
                        ];
            }
            """,
            Notes:
            "OnMountAsync runs once on first render. The framework's async lifecycle handler triggers a re-render when the awaited task completes. Component.CancellationToken cancels on unmount — navigate away mid-fetch and the in-flight request aborts.",
            Result: RenderResult()),
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

    private Component RenderResult()
    {
        if (_error is not null)
        {
            return Div(Class: "alert alert-danger mb-0")[
                Strong()["Error: "], _error
            ];
        }

        if (_post is null)
        {
            return Div(Class: "text-secondary d-flex align-items-center")[
                Span(Class: "spinner-border spinner-border-sm me-2"),
                "Loading…"
            ];
        }

        return Article(Class: "card border-0 bg-light")[
            Div(Class: "card-body")[
                Div(Class: "small text-secondary text-uppercase mb-1")[$"Post #{_post.Id}"],
                H3(Class: "h6 fw-semibold")[_post.Title],
                P(Class: "mb-0 small")[_post.Body]
            ]
        ];
    }

    public sealed record Post(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body);
}

[JsonSerializable(typeof(HttpPage.Post))]
internal sealed partial class HttpJsonContext : JsonSerializerContext;
