using Rask.Cqrs;

namespace Rask.Example.Auth.WasmCookie.Features;

// Remote dispatch from the browser, over the same IDispatcher call a server-rendered page would make
// in-process. There is no HttpClient here and no url: the query travels because its handler lives in
// the host, and the HttpOnly auth cookie rides the request because it is same-origin — which is why
// the server can answer with the identity it sees rather than one the message carried.
public sealed partial class RemoteDispatchPanel(IDispatcher dispatcher) : Component
{
    private ServerIdentity? _seen;
    private int _visits;

    protected override async Task OnMountAsync() =>
        _seen = await dispatcher.QueryAsync(new WhoAmI(), CancellationToken);

    private async Task NoteVisitAsync() =>
        _visits = await dispatcher.SendAsync(new NoteVisit(), CancellationToken);

    protected override Component? Render() =>
        Div.Id("cqrs-panel").Class("mt-4 rounded-lg bg-slate-50 px-4 py-3 dark:bg-slate-900")[
            P.Id("cqrs-whoami").Class("mb-2 text-sm")[
                _seen is null
                    ? "Asking the server who is calling…"
                    : $"The server sees {_seen.Name} ({string.Join(", ", _seen.Roles)})."],
            Button.Id("cqrs-visit").Type("button")
                .OnClickAsync(NoteVisitAsync)
                .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium transition bg-violet-600 text-white hover:bg-violet-700")[
                    "Note a visit"],
            Span.Id("cqrs-visits").Class("ml-2 text-sm text-slate-600 dark:text-slate-300")[$"{_visits}"]
        ];
}
