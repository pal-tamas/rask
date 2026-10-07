using System.Globalization;

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
            Ui.Input.Bind(() => _model.OptionalAge).Label("Optional age (int?)")
                .Id("bind-null-age")
                .Description("Leave it empty for null.")
        ],
        Div.Class("mb-3")[
            Ui.Input.Bind(() => _model.StartDate).Label("Optional start date (DateOnly?)")
                .Id("bind-null-start")
        ],
        Div.Class("mb-3")[
            // "— none —" is a real option rather than a Placeholder: choosing it clears the value back to null,
            // which is the point of this demo. A placeholder cannot be chosen.
            Ui.Select.Bind(() => _model.Favorite)
                .Label("Optional colour (Color?)")
                .Id("bind-null-color")[
                Colors.Select(color => Ui.SelectOption.Key(color.Text).Value(color.Value)[color.Text])
            ]
        ],
        Div.Class("mb-3")[
            Ui.Input.Bind(() => _model.Nickname).Label("Nickname (string?)")
                .Id("bind-null-nick")
                .Description("Clear it for null.")
        ],
        Pre.Class("text-sm mb-0 p-3 bg-ui-well border rounded")[
            Code[
                $"OptionalAge = {_model.OptionalAge?.ToString(CultureInfo.InvariantCulture) ?? "null"}\n" +
                $"StartDate   = {_model.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null"}\n" +
                $"Favorite    = {_model.Favorite?.ToString() ?? "null"}\n" +
                $"Nickname    = {(_model.Nickname is null ? "null" : "\"" + _model.Nickname + "\"")}"
            ]
        ]
    ];

    public sealed class Holder
    {
        public int? OptionalAge { get; set; }
        public DateOnly? StartDate { get; set; }
        public Color? Favorite { get; set; }
        public string? Nickname { get; set; }
    }
}
