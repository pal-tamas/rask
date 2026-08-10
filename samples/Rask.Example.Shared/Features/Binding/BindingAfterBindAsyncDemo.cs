namespace Rask.Example.Shared.Features;

public sealed partial class BindingAfterBindAsyncDemo : Component
{
    private static readonly Dictionary<string, string[]> _catalog = new()
    {
        ["frontend"] = ["TypeScript", "JavaScript", "HTML", "CSS"],
        ["backend"] = ["C#", "Rust", "Go", "Python"],
        ["data"] = ["SQL", "Python", "R", "Scala"]
    };

    private readonly Holder _model = new();
    private string[] _languages = [];
    private bool _loading;

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            Label.For("bind-async-track").Class("form-label small")["Track"],
            Select(() => _model.Track)
                .AfterBindAsync(async track =>
                {
                    // Re-selecting the placeholder (or any unknown track) clears the
                    // dependent list instead of throwing on _catalog[track].
                    if (!_catalog.ContainsKey(track))
                    {
                        _languages = [];
                        _model.Language = "";
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
                .Class("form-select")[
                // Placeholder matching the empty initial Track. Without it the <select>
                // visually defaults to "Frontend" while the model is still "" — and
                // re-picking the already-shown first option fires no change event, so the
                // async load never triggers. A selected placeholder keeps the initial
                // display honest and makes every track pick a real change.
                Option.Value("")["— pick a track —"],
                Option.Value("frontend")["Frontend"],
                Option.Value("backend")["Backend"],
                Option.Value("data")["Data"]
            ]
        ],
        Div.Class("mb-3")[
            Label.For("bind-async-lang").Class("form-label small")[
                _loading ? "Language (loading…)" : "Language"
            ],
            Select(() => _model.Language)
                .Id("bind-async-lang")
                .Class("form-select")
                .Disabled(_loading || _languages.Length == 0)[
                _languages.Length == 0
                    ? [Option.Value("")["— pick a track —"]]
                    : _languages.Select(l => Option.Value(l).Key(l)[l])
            ]
        ],
        Pre.Class("small mb-0 p-3 bg-light border rounded")[
            Code.Id("bind-async-echo")[
                $"Track    = {_model.Track}\n" +
                $"Language = {_model.Language}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public string Track { get; set; } = "";
        public string Language { get; set; } = "";
    }
}
