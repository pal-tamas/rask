namespace Rask.Example.Shared.Features;

public sealed class BindingMultiDemo : Component
{
    public enum Color { Red, Green, Blue }

    private readonly Holder _model = new();

    protected override Component? Render() =>
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
