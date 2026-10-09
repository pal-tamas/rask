namespace Rask.Site.Features;

public sealed partial class BindingMultiDemo : Component
{
    public enum Color { Red, Green, Blue }

    private static readonly (Color Value, string Text)[] Colors =
        [(Color.Red, "Red"), (Color.Green, "Green"), (Color.Blue, "Blue")];

    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            Ui.Checkbox.Bind(() => _model.Subscribe).Id("bind-subscribe").Label("Subscribe to the newsletter")
        ],
        Div.Class("mb-3")[
            Ui.Input.Bind(() => _model.Age).Live().Label("Age")
                .Id("bind-age")
                .Min("0")
                .Max("120")
        ],
        Div.Class("mb-3")[
            Ui.Input.Bind(() => _model.StartDate).Live().Label("Start date")
                .Id("bind-start")
        ],
        Div.Class("mb-3")[
            Ui.Select.Bind(() => _model.Favorite)
                .Label("Favourite colour")
                .Id("bind-favorite")[
                Colors.Select(color => Ui.SelectOption.Key(color.Text).Value(color.Value)[color.Text])
            ]
        ],
        Pre.Class("text-sm mb-0 p-3 bg-ui-well border rounded")[
            Code[
                $"Subscribe = {(_model.Subscribe ? "true" : "false")}\n" +
                $"Age       = {_model.Age}\n" +
                $"StartDate = {_model.StartDate:yyyy-MM-dd}\n" +
                $"Favorite  = {_model.Favorite}"
            ]
        ]
    ];

    public sealed class Holder
    {
        public bool Subscribe { get; set; }
        public int Age { get; set; } = 30;
        public DateOnly StartDate { get; set; } = new(2026, 1, 1);
        public Color Favorite { get; set; } = Color.Blue;
    }
}
