using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Rask.Example.Shared.Features;

// HttpClient is registered as a service in Program.cs and injected through the primary
// constructor. OnMountAsync runs once on first render; the framework's async lifecycle handler
// triggers a re-render when the awaited task completes. Component.CancellationToken cancels on
// unmount — navigate away mid-fetch and the in-flight request aborts.
public sealed partial class HttpFetchDemo(HttpClient http) : Component
{
    private const int MaxTransientRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);

    private string? _error;
    private Post? _post;

    protected override async Task OnMountAsync()
    {
        for (var attempt = 0; ; attempt++)
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

    protected override Component? Render()
    {
        if (_error is not null)
        {
            return Div.Class($"{Ui.AlertDanger} mb-0")[
                Strong["Error: "], _error
            ];
        }

        if (_post is null)
        {
            return Div.Class("text-slate-500 dark:text-slate-400 flex items-center")[
                Span.Class($"{Ui.Spinner} size-4 me-2"),
                "Loading…"
            ];
        }

        return Article.Class($"{Ui.Card} border-0 bg-slate-100")[
            Div.Class(Ui.CardBody)[
                Div.Class("text-sm text-slate-500 dark:text-slate-400 uppercase mb-1")[$"Post #{_post.Id}"],
                H3.Class("text-base font-semibold")[_post.Title],
                P.Class("mb-0 text-sm")[_post.Body]
            ]
        ];
    }

    public sealed record Post(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body);
}

[JsonSerializable(typeof(HttpFetchDemo.Post))]
internal sealed partial class HttpJsonContext : JsonSerializerContext;
