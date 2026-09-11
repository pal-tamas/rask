namespace Rask.Site.Features;

public sealed partial class BindingAfterBindAsyncDemo : Component
{
    private static readonly Dictionary<string, string[]> _catalog = new()
    {
        ["frontend"] = ["TypeScript", "JavaScript", "HTML", "CSS"],
        ["backend"] = ["C#", "Rust", "Go", "Python"],
        ["data"] = ["SQL", "Python", "R", "Scala"]
    };

    private static readonly (string? Value, string Text)[] Tracks =
        [("frontend", "Frontend"), ("backend", "Backend"), ("data", "Data")];

    private readonly Holder _model = new();
    private string[] _languages = [];
    private bool _loading;

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            UiSelect.Bind(() => _model.Track)
                .Options(Tracks)
                // The placeholder is selected while Track is still null. Without it the <select> would
                // visually default to "Frontend" while the model holds nothing — and re-picking the
                // already-shown first option fires no change event, so the async load would never trigger.
                .Placeholder("— pick a track —")
                .Label("Track")
                .AfterBind(async track =>
                {
                    // An unknown track clears the dependent list instead of throwing on _catalog[track].
                    if (track is null || !_catalog.ContainsKey(track))
                    {
                        _languages = [];
                        _model.Language = null;
                        _loading = false;
                        return;
                    }

                    // Rask re-renders at every await suspension inside an async handler, so
                    // flipping _loading before the await below is enough to surface the
                    // "loading…" UI — a manual StateHasChanged() here would only set a deferred
                    // in-handler flag and push no frame.
                    _loading = true;
                    // Simulated remote fetch — swap for HttpClient.GetFromJsonAsync in real code.
                    // Pass the component's CancellationToken so unmount-during-fetch aborts
                    // the simulated work cleanly instead of mutating state on a stale instance.
                    try
                    {
                        await Task.Delay(300, CancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    _languages = _catalog[track];
                    _model.Language = _languages[0];
                    _loading = false;
                })
                .Id("bind-async-track")
        ],
        Div.Class("mb-3")[
            UiSelect.Bind(() => _model.Language)
                .Options([.. _languages.Select(l => ((string?)l, l))])
                .Placeholder("— pick a track —")
                .Label(_loading ? "Language (loading…)" : "Language")
                .Id("bind-async-lang")
                .Disabled(_loading || _languages.Length == 0)
        ],
        Pre.Class("text-sm mb-0 p-3 bg-ui-well border rounded")[
            Code.Id("bind-async-echo")[
                $"Track    = {_model.Track}\n" +
                $"Language = {_model.Language}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public string? Track { get; set; }
        public string? Language { get; set; }
    }
}
