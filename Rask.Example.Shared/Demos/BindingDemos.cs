namespace Rask.Example.Shared.Demos;

// Each live demo is its own user component so the bound input's auto-registered
// handler owner resolves to *this* demo (the structural CurrentParent at handler
// registration). Without this wrapper the owner falls back to CodeSample, which
// re-renders only itself and never re-evaluates the page's state.

public sealed class BindingManualDemo : Component
{
    private string _typed = "";

    protected override Component Render() =>
        Fragment()[
            Input(
                "text",
                Class: "form-control mb-2",
                Placeholder: "Type something",
                Value: _typed,
                OnInput: v => _typed = v),
            P(Class: "small mb-0")[
                "Echo: ",
                Code()[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
            ]];
}

public sealed class BindingTypedDemo : Component
{
    private readonly Holder _model = new();

    protected override Component Render() =>
        Fragment()[
            Input(
                () => _model.Name,
                Class: "form-control mb-2",
                Placeholder: "Your name"),
            P(Class: "small mb-0")[
                "Hello, ",
                Strong()[string.IsNullOrEmpty(_model.Name) ? "stranger" : _model.Name],
                "!"
            ]];

    private sealed class Holder
    {
        public string Name { get; set; } = "";
    }
}

public sealed class BindingTextareaDemo : Component
{
    private readonly Holder _model = new();

    protected override Component Render() =>
        Fragment()[
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
            ]];

    private sealed class Holder
    {
        public string Notes { get; set; } = "";
    }
}

public sealed class BindingNullableDemo : Component
{
    public enum Color { Red, Green, Blue }

    private readonly Holder _model = new();

    protected override Component Render() =>
        Fragment()[
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
                        Option(Value: "")["— none —"],
                        Option(Value: "Red")["Red"],
                        Option(Value: "Green")["Green"],
                        Option(Value: "Blue")["Blue"]
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
                    $"OptionalAge = {(_model.OptionalAge?.ToString() ?? "null")}\n" +
                    $"StartDate   = {(_model.StartDate?.ToString("yyyy-MM-dd") ?? "null")}\n" +
                    $"Favorite    = {(_model.Favorite?.ToString() ?? "null")}\n" +
                    $"Nickname    = {(_model.Nickname is null ? "null" : "\"" + _model.Nickname + "\"")}"
                ]
            ]];

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

    protected override Component Render() =>
        Fragment()[
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
                Code(Id: "bind-clear-echo")[
                    $"Age         = {_model.Age}\n" +
                    $"OptionalAge = {(_model.OptionalAge?.ToString() ?? "null")}"
                ]
            ]];

    private sealed class Holder
    {
        public int Age { get; set; } = 30;
        public int? OptionalAge { get; set; } = 7;
    }
}

public sealed class BindingAfterBindDemo : Component
{
    private readonly Holder _model = new();
    private string[] _cities = Cities["US"];

    private static readonly Dictionary<string, string[]> Cities = new()
    {
        ["US"] = new[] { "New York", "Los Angeles", "Chicago" },
        ["DE"] = new[] { "Berlin", "Hamburg", "Munich" },
        ["JP"] = new[] { "Tokyo", "Osaka", "Kyoto" }
    };

    protected override Component Render() =>
        Fragment()[
            Div(Class: "mb-3")[
                Label("bind-after-country", Class: "form-label small")["Country"],
                Select(
                    Bind: () => _model.Country,
                    AfterBind: c =>
                    {
                        _cities = Cities[c];
                        _model.City = _cities[0];
                    },
                    Id: "bind-after-country",
                    Class: "form-select")[
                        Option(Value: "US")["United States"],
                        Option(Value: "DE")["Germany"],
                        Option(Value: "JP")["Japan"]
                    ]
            ],
            Div(Class: "mb-3")[
                Label("bind-after-city", Class: "form-label small")["City"],
                Select(
                    Bind: () => _model.City,
                    Id: "bind-after-city",
                    Class: "form-select")[
                        _cities.Select(c => Option(Value: c)[c])
                    ]
            ],
            Pre(Class: "small mb-0 p-3 bg-light border rounded")[
                Code(Id: "bind-after-echo")[
                    $"Country = {_model.Country}\n" +
                    $"City    = {_model.City}"
                ]
            ]];

    private sealed class Holder
    {
        public string Country { get; set; } = "US";
        public string City { get; set; } = "New York";
    }
}

public sealed class BindingAfterBindAsyncDemo : Component
{
    private readonly Holder _model = new();
    private string[] _languages = [];
    private bool _loading;

    private static readonly Dictionary<string, string[]> _catalog = new()
    {
        ["frontend"] = ["TypeScript", "JavaScript", "HTML", "CSS"],
        ["backend"]  = ["C#", "Rust", "Go", "Python"],
        ["data"]     = ["SQL", "Python", "R", "Scala"]
    };

    protected override Component Render() =>
        Fragment()[
            Div(Class: "mb-3")[
                Label("bind-async-track", Class: "form-label small")["Track"],
                Select(
                    Bind: () => _model.Track,
                    AfterBindAsync: async track =>
                    {
                        _loading = true;
                        await StateHasChangedAsync();
                        // Simulated remote fetch — swap for HttpClient.GetFromJsonAsync in real code.
                        await Task.Delay(300);
                        _languages = _catalog[track];
                        _model.Language = _languages[0];
                        _loading = false;
                    },
                    Id: "bind-async-track",
                    Class: "form-select")[
                        Option(Value: "frontend")["Frontend"],
                        Option(Value: "backend")["Backend"],
                        Option(Value: "data")["Data"]
                    ]
            ],
            Div(Class: "mb-3")[
                Label("bind-async-lang", Class: "form-label small")[
                    _loading ? "Language (loading…)" : "Language"
                ],
                Select(
                    Bind: () => _model.Language,
                    Id: "bind-async-lang",
                    Class: "form-select",
                    Disabled: _loading || _languages.Length == 0)[
                        _languages.Length == 0
                            ? [Option(Value: "")["— pick a track —"]]
                            : _languages.Select(l => Option(Value: l)[l])
                    ]
            ],
            Pre(Class: "small mb-0 p-3 bg-light border rounded")[
                Code(Id: "bind-async-echo")[
                    $"Track    = {_model.Track}\n" +
                    $"Language = {_model.Language}"
                ]
            ]];

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

    protected override Component Render() =>
        Fragment()[
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
            ]];

    // Bound by Input/Select via expression trees; Rider doesn't see the setter
    // path so its "unused setter" cleanup will silently strip these — keep
    // {get;set;} explicit and don't let the IDE downgrade them.
    private sealed class Holder
    {
        public bool Subscribe { get; set; }
        public int Age { get; set; } = 30;
        public DateOnly StartDate { get; set; } = new(2026, 1, 1);
        public Color Favorite { get; set; } = Color.Blue;
    }
}
