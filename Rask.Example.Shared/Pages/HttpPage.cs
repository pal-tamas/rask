using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("http")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class HttpPage(HttpClient http) : Component
{
    private string? _error;
    private Post? _post;

    protected override async Task OnInitializedAsync()
    {
        try { _post = await http.GetFromJsonAsync<Post>("posts/1"); }
        catch (Exception ex) { _error = ex.Message; }
    }

    public override Component Render() =>
        Fragment(
            PageHeader.Render(
                "HttpClient + DI",
                "HttpClient is registered as a service in Program.cs and injected into pages through their primary constructor. This demo fetches from jsonplaceholder.typicode.com — a public CORS-friendly API."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Register"]),
            Components.CodeSample(
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
            H2(Class: "h4 mt-5 mb-3", Children: ["Inject and fetch"]),
            Components.CodeSample(
                """
                [Route("/http")]
                public sealed class HttpPage(HttpClient http) : Component
                {
                    private Post? _post;

                    protected override async Task OnInitializedAsync() =>
                        _post = await http.GetFromJsonAsync<Post>("posts/1");

                    public override Component Render() =>
                        _post is null
                            ? P(Children: [Em(Children: ["Loading…"])])
                            : Article(Children: [
                                H3(Children: [_post.Title]),
                                P(Children: [_post.Body])
                            ]);
                }
                """,
                Notes:
                "OnInitializedAsync runs once on first render. The framework's async lifecycle handler triggers a re-render when the awaited task completes.",
                Result: RenderResult()),
            Div(Class: "alert alert-info d-flex align-items-start mt-4", Children:
            [
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div(Children:
                [
                    Strong(Children: ["Same demo, two hosts."]),
                    " Under ", Code(Children: ["Rask.Example.Server"]),
                    " the request goes server-to-server. Under ",
                    Code(Children: ["Rask.Example.Wasm"]),
                    " (and the GitHub Pages deploy) it runs from the browser via CORS. ",
                    "The page code is identical."
                ])
            ])
        );

    private Component RenderResult()
    {
        if (_error is not null)
        {
            return Div(Class: "alert alert-danger mb-0", Children:
            [
                Strong(Children: ["Error: "]), _error
            ]);
        }

        if (_post is null)
        {
            return Div(Class: "text-secondary d-flex align-items-center", Children:
            [
                Span(Class: "spinner-border spinner-border-sm me-2", Children: []),
                "Loading…"
            ]);
        }

        return Article(Class: "card border-0 bg-light", Children:
        [
            Div(Class: "card-body", Children:
            [
                Div(Class: "small text-secondary text-uppercase mb-1", Children: [$"Post #{_post.Id}"]),
                H3(Class: "h6 fw-semibold", Children: [_post.Title]),
                P(Class: "mb-0 small", Children: [_post.Body])
            ])
        ]);
    }

    public sealed record Post(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body);
}
