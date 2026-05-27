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
    private string? _error;
    private Post? _post;

    protected override RenderResult Head => Title()["HttpClient + DI — Rask"];

    protected override async Task OnMountAsync()
    {
        try { _post = await http.GetFromJsonAsync("posts/1", HttpJsonContext.Default.Post, CancellationToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _error = ex.Message; }
    }

    protected override RenderResult Render() =>
        [
            PageHeader.Render(
                "HttpClient + DI",
                "HttpClient is registered as a service in Program.cs and injected into pages through their primary constructor. This demo fetches from jsonplaceholder.typicode.com — a public CORS-friendly API."),
            H2(Class: "h4 mt-4 mb-3")["Register"],
            CodeSample(
                """
                // Program.cs
                var host = WasmHostBuilder.CreateDefault();
                host.Services.AddSingleton(_ =>
                    new HttpClient {
                        BaseAddress = new Uri("https://jsonplaceholder.typicode.com/")
                    });
                await host.RunAsync<App>();
                """,
                Notes:
                "Relative URLs require BaseAddress. For relative-to-page-origin, use new Uri(WasmHostBuilder.BaseAddress) — read lazily inside the factory so it fires after the JS module imports."),
            H2(Class: "h4 mt-5 mb-3")["Inject and fetch"],
            CodeSample(
                """
                [Route("/http")]
                public sealed class HttpPage(HttpClient http) : Component
                {
                    private Post? _post;

                    protected override async Task OnMountAsync() =>
                        _post = await http.GetFromJsonAsync<Post>("posts/1", CancellationToken);

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
                    " the request goes server-to-server. Under ",
                    Code()["Rask.Example.Wasm"],
                    " (and the GitHub Pages deploy) it runs from the browser via CORS. ",
                    "The page code is identical."
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
