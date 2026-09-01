using Rask.Cqrs;

namespace Rask.Example.Auth.WasmCookie.Features;

// Remote dispatch from the browser, over the same IDispatcher call a server-rendered page would make
// in-process. There is no HttpClient here and no url: the query travels because its handler lives in
// the host, and the HttpOnly auth cookie rides the request because it is same-origin — which is why
// the server can answer with the identity it sees rather than one the message carried.
public sealed partial class RemoteDispatchPanel(IDispatcher dispatcher) : Component
{
    private ServerIdentity? _seen;
    private string? _refused;
    private int _visits;

    protected override Task OnMountAsync() => AskAsync();

    // RemoteDispatchException is the ONE thing remote dispatch adds to an in-process call, so a demo of
    // remote dispatch has to show it. This panel renders behind the Authorize gate, which opens on what
    // /api/me said — and a cookie that expired since then makes the server answer 401 to a page that
    // believes it is signed in. Left to propagate out of a lifecycle hook it would reach
    // RootErrorBoundary and replace the whole page, which reads as "the app broke" rather than "your
    // session ended". ApiUserProvider catches its own transport failures for the same reason.
    private async Task AskAsync()
    {
        try
        {
            _seen = await dispatcher.QueryAsync(new WhoAmI(), CancellationToken);
            _refused = null;
        }
        catch (RemoteDispatchException ex)
        {
            // A null StatusCode is the distinctive case: the request never got an answer at all, so
            // there is no status to report and the cause is the inner exception.
            _refused = ex.StatusCode is { } status
                ? $"The server refused the dispatch ({status})."
                : "The server could not be reached.";
        }
    }

    private async Task NoteVisitAsync()
    {
        try
        {
            _visits = await dispatcher.SendAsync(new NoteVisit(), CancellationToken);
            _refused = null;
        }
        catch (RemoteDispatchException ex)
        {
            _refused = ex.StatusCode is { } status
                ? $"The visit was not recorded ({status})."
                : "The visit could not be sent.";
        }
    }

    protected override Component? Render() =>
        Div.Id("cqrs-panel").Class("mt-4 rounded-lg bg-slate-50 px-4 py-3 dark:bg-slate-900")[
            P.Id("cqrs-whoami").Class("mb-2 text-sm")[
                _refused
                ?? (_seen is null
                    ? "Asking the server who is calling…"
                    : $"The server sees {_seen.Name} ({string.Join(", ", _seen.Roles)}).")],
            Button.Id("cqrs-visit").Type("button")
                .OnClickAsync(NoteVisitAsync)
                .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium transition bg-violet-600 text-white hover:bg-violet-700")[
                    "Note a visit"],
            Span.Id("cqrs-visits").Class("ml-2 text-sm text-slate-600 dark:text-slate-300")[$"{_visits}"]
        ];
}
