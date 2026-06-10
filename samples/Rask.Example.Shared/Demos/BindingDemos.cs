namespace Rask.Example.Shared.Demos;

// Each live demo is its own user component so the bound input's auto-registered
// handler owner resolves to *this* demo (the structural CurrentParent at handler
// registration). Without this wrapper the owner falls back to CodeSample, which
// re-renders only itself and never re-evaluates the page's state.

public sealed class BindingManualDemo : Component
{
    private string _typed = "";

    protected override RenderResult Render() =>
    [
        Input(
            "text",
            Class: "form-control mb-2",
            Placeholder: "Type something",
            Value: _typed,
            OnInput: v => _typed = v),
        P(Class: "small mb-0")[
            "Echo: ",
            Code()[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
        ]
    ];
}

public sealed class BindingTypedDemo : Component
{
    private readonly Holder _model = new();

    protected override RenderResult Render() =>
    [
        Input(
            () => _model.Name,
            Class: "form-control mb-2",
            Placeholder: "Your name"),
        P(Class: "small mb-0")[
            "Hello, ",
            Strong()[string.IsNullOrEmpty(_model.Name) ? "stranger" : _model.Name],
            "!"
        ]
    ];

    private sealed class Holder
    {
        public string Name { get; set; } = "";
    }
}

public sealed class BindingTextareaDemo : Component
{
    private readonly Holder _model = new();

    protected override RenderResult Render() =>
    [
        Textarea(
            () => _model.Notes,
            Id: "bind-textarea",
            Class: "form-control mb-2",
            Rows: 3,
            Placeholder: "Jot something down…"),
        Pre(Class: "small mb-0 p-3 bg-light border rounded")[
            Code()[
                $"Notes  = \"{_model.Notes}\"\n" +
                $"Length = {_model.Notes.Length}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public string Notes { get; set; } = "";
    }
}

public sealed class BindingNullableDemo : Component
{
    public enum Color { Red, Green, Blue }

    private readonly Holder _model = new();

    protected override RenderResult Render() =>
    [
        Div(Class: "mb-3")[
            Label("bind-null-age", Class: "form-label small")["Optional age (int?)"],
            Input(
                () => _model.OptionalAge,
                Id: "bind-null-age",
                Class: "form-control",
                Placeholder: "leave empty for null")
        ],
        Div(Class: "mb-3")[
            Label("bind-null-start", Class: "form-label small")["Optional start date (DateOnly?)"],
            Input(
                () => _model.StartDate,
                Id: "bind-null-start",
                Class: "form-control")
        ],
        Div(Class: "mb-3")[
            Label("bind-null-color", Class: "form-label small")["Optional colour (Color?)"],
            Select(
                () => _model.Favorite,
                Id: "bind-null-color",
                Class: "form-select",
                Children: new Child[]
                {
                    Option("")["— none —"], Option("Red")["Red"], Option("Green")["Green"], Option("Blue")["Blue"]
                })
        ],
        Div(Class: "mb-3")[
            Label("bind-null-nick", Class: "form-label small")["Nickname (string?)"],
            Input(
                () => _model.Nickname,
                Id: "bind-null-nick",
                Class: "form-control",
                Placeholder: "clear me for null")
        ],
        Pre(Class: "small mb-0 p-3 bg-light border rounded")[
            Code()[
                $"OptionalAge = {_model.OptionalAge?.ToString() ?? "null"}\n" +
                $"StartDate   = {_model.StartDate?.ToString("yyyy-MM-dd") ?? "null"}\n" +
                $"Favorite    = {_model.Favorite?.ToString() ?? "null"}\n" +
                $"Nickname    = {(_model.Nickname is null ? "null" : "\"" + _model.Nickname + "\"")}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public int? OptionalAge { get; set; }
        public DateOnly? StartDate { get; set; }
        public Color? Favorite { get; set; }
        public string? Nickname { get; set; }
    }
}

public sealed class BindingClearDefaultDemo : Component
{
    private readonly Holder _model = new();

    protected override RenderResult Render() =>
    [
        Div(Class: "mb-3")[
            Label("bind-clear-age", Class: "form-label small")["Age (non-nullable int) — clear → 0"],
            Input(
                () => _model.Age,
                Id: "bind-clear-age",
                Class: "form-control")
        ],
        Div(Class: "mb-3")[
            Label("bind-clear-optage", Class: "form-label small")["Optional age (int?) — clear → null"],
            Input(
                () => _model.OptionalAge,
                Id: "bind-clear-optage",
                Class: "form-control",
                Placeholder: "leave empty for null")
        ],
        Pre(Class: "small mb-0 p-3 bg-light border rounded")[
            Code("bind-clear-echo")[
                $"Age         = {_model.Age}\n" +
                $"OptionalAge = {_model.OptionalAge?.ToString() ?? "null"}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public int Age { get; set; } = 30;
        public int? OptionalAge { get; set; } = 7;
    }
}

public sealed class BindingAfterBindDemo : Component
{
    private static readonly Dictionary<string, string[]> Cities = new()
    {
        ["US"] = new[] { "New York", "Los Angeles", "Chicago" },
        ["DE"] = new[] { "Berlin", "Hamburg", "Munich" },
        ["JP"] = new[] { "Tokyo", "Osaka", "Kyoto" }
    };

    private readonly Holder _model = new();
    private string[] _cities = Cities["US"];

    protected override RenderResult Render() =>
    [
        Div(Class: "mb-3")[
            Label("bind-after-country", Class: "form-label small")["Country"],
            Select(
                () => _model.Country,
                c =>
                {
                    _cities = Cities[c];
                    _model.City = _cities[0];
                },
                Id: "bind-after-country",
                Class: "form-select")[
                Option("US")["United States"],
                Option("DE")["Germany"],
                Option("JP")["Japan"]
            ]
        ],
        Div(Class: "mb-3")[
            Label("bind-after-city", Class: "form-label small")["City"],
            Select(
                () => _model.City,
                Id: "bind-after-city",
                Class: "form-select")[
                _cities.Select(c => Option(c, Key: c)[c])
            ]
        ],
        Pre(Class: "small mb-0 p-3 bg-light border rounded")[
            Code("bind-after-echo")[
                $"Country = {_model.Country}\n" +
                $"City    = {_model.City}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public string Country { get; set; } = "US";
        public string City { get; set; } = "New York";
    }
}

public sealed class BindingAfterBindAsyncDemo : Component
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

    protected override RenderResult Render() =>
    [
        Div(Class: "mb-3")[
            Label("bind-async-track", Class: "form-label small")["Track"],
            Select(
                () => _model.Track,
                AfterBindAsync: async track =>
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
                },
                Id: "bind-async-track",
                Class: "form-select")[
                // Placeholder matching the empty initial Track. Without it the <select>
                // visually defaults to "Frontend" while the model is still "" — and
                // re-picking the already-shown first option fires no change event, so the
                // async load never triggers. A selected placeholder keeps the initial
                // display honest and makes every track pick a real change.
                Option("")["— pick a track —"],
                Option("frontend")["Frontend"],
                Option("backend")["Backend"],
                Option("data")["Data"]
            ]
        ],
        Div(Class: "mb-3")[
            Label("bind-async-lang", Class: "form-label small")[
                _loading ? "Language (loading…)" : "Language"
            ],
            Select(
                () => _model.Language,
                Id: "bind-async-lang",
                Class: "form-select",
                Disabled: _loading || _languages.Length == 0)[
                _languages.Length == 0
                    ? [Option("")["— pick a track —"]]
                    : _languages.Select(l => Option(l, Key: l)[l])
            ]
        ],
        Pre(Class: "small mb-0 p-3 bg-light border rounded")[
            Code("bind-async-echo")[
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

public sealed class BindingMultiDemo : Component
{
    public enum Color { Red, Green, Blue }

    private readonly Holder _model = new();

    protected override RenderResult Render() =>
    [
        Div(Class: "mb-3 form-check")[
            Input(
                () => _model.Subscribe,
                Id: "bind-subscribe",
                Class: "form-check-input"),
            Label("bind-subscribe", Class: "form-check-label ms-1")["Subscribe to the newsletter"]
        ],
        Div(Class: "mb-3")[
            Label("bind-age", Class: "form-label small")["Age"],
            Input(
                () => _model.Age,
                Id: "bind-age",
                Class: "form-control",
                Min: "0",
                Max: "120")
        ],
        Div(Class: "mb-3")[
            Label("bind-start", Class: "form-label small")["Start date"],
            Input(
                () => _model.StartDate,
                Id: "bind-start",
                Class: "form-control")
        ],
        Div(Class: "mb-3")[
            Label("bind-favorite", Class: "form-label small")["Favourite colour"],
            Select(
                () => _model.Favorite,
                Id: "bind-favorite",
                Class: "form-select")[
                Option("Red")["Red"],
                Option("Green")["Green"],
                Option("Blue")["Blue"]
            ]
        ],
        Pre(Class: "small mb-0 p-3 bg-light border rounded")[
            Code()[
                $"Subscribe = {(_model.Subscribe ? "true" : "false")}\n" +
                $"Age       = {_model.Age}\n" +
                $"StartDate = {_model.StartDate:yyyy-MM-dd}\n" +
                $"Favorite  = {_model.Favorite}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public bool Subscribe { get; set; }
        public int Age { get; set; } = 30;
        public DateOnly StartDate { get; set; } = new(2026, 1, 1);
        public Color Favorite { get; set; } = Color.Blue;
    }
}
