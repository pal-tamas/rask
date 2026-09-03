namespace Rask.Example.Shared.Features;

public sealed partial class BindingNullableDemo : Component
{
    public enum Color { Red, Green, Blue }

    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            Label.For("bind-null-age").Class($"{Tw.Label} text-sm")["Optional age (int?)"],
            Input.Bind(() => _model.OptionalAge)
                .Id("bind-null-age")
                .Class(Tw.Input)
                .Placeholder("leave empty for null")
        ],
        Div.Class("mb-3")[
            Label.For("bind-null-start").Class($"{Tw.Label} text-sm")["Optional start date (DateOnly?)"],
            Input.Bind(() => _model.StartDate)
                .Id("bind-null-start")
                .Class(Tw.Input)
        ],
        Div.Class("mb-3")[
            Label.For("bind-null-color").Class($"{Tw.Label} text-sm")["Optional colour (Color?)"],
            Select.Bind(() => _model.Favorite)
                .Id("bind-null-color")
                .Class(Tw.Select)[
                Option.Value("")["— none —"], Option.Value("Red")["Red"], Option.Value("Green")["Green"], Option.Value("Blue")["Blue"]
            ]
        ],
        Div.Class("mb-3")[
            Label.For("bind-null-nick").Class($"{Tw.Label} text-sm")["Nickname (string?)"],
            Input.Bind(() => _model.Nickname)
                .Id("bind-null-nick")
                .Class(Tw.Input)
                .Placeholder("clear me for null")
        ],
        Pre.Class("text-sm mb-0 p-3 bg-ui-well border rounded")[
            Code[
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
