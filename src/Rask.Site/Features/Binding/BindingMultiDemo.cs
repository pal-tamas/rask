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
            UiCheckbox.Bind(() => _model.Subscribe).Text("Subscribe to the newsletter").Id("bind-subscribe")
        ],
        Div.Class("mb-3")[
            UiInput.Bind(() => _model.Age).Label("Age")
                .Id("bind-age")
                .Min("0")
                .Max("120")
        ],
        Div.Class("mb-3")[
            UiInput.Bind(() => _model.StartDate).Label("Start date")
                .Id("bind-start")
        ],
        Div.Class("mb-3")[
            UiSelect.Bind(() => _model.Favorite)
                .Options(Colors)
                .Label("Favourite colour")
                .Id("bind-favorite")
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

    private sealed class Holder
    {
        public bool Subscribe { get; set; }
        public int Age { get; set; } = 30;
        public DateOnly StartDate { get; set; } = new(2026, 1, 1);
        public Color Favorite { get; set; } = Color.Blue;
    }
}
