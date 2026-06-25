namespace Rask.Example.Shared.Features;

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
                Class: "form-select")[
                Option("")["— none —"], Option("Red")["Red"], Option("Green")["Green"], Option("Blue")["Blue"]
            ]
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
