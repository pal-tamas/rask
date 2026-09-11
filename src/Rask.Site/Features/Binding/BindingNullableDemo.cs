namespace Rask.Site.Features;

public sealed partial class BindingNullableDemo : Component
{
    public enum Color { Red, Green, Blue }

    private static readonly (Color? Value, string Text)[] Colors =
        [(null, "— none —"), (Color.Red, "Red"), (Color.Green, "Green"), (Color.Blue, "Blue")];

    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            UiInput.Bind(() => _model.OptionalAge).Label("Optional age (int?)")
                .Id("bind-null-age")
                .Placeholder("leave empty for null")
        ],
        Div.Class("mb-3")[
            UiInput.Bind(() => _model.StartDate).Label("Optional start date (DateOnly?)")
                .Id("bind-null-start")
        ],
        Div.Class("mb-3")[
            // "— none —" is a real option rather than a Placeholder: choosing it clears the value back to null,
            // which is the point of this demo. A placeholder cannot be chosen.
            UiSelect.Bind(() => _model.Favorite)
                .Options(Colors)
                .Label("Optional colour (Color?)")
                .Id("bind-null-color")
        ],
        Div.Class("mb-3")[
            UiInput.Bind(() => _model.Nickname).Label("Nickname (string?)")
                .Id("bind-null-nick")
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
