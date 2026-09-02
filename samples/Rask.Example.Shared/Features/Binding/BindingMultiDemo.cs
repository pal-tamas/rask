namespace Rask.Example.Shared.Features;

public sealed partial class BindingMultiDemo : Component
{
    public enum Color { Red, Green, Blue }

    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Div.Class("mb-3 flex items-center gap-2")[
            Input.Bind(() => _model.Subscribe)
                .Id("bind-subscribe")
                .Class(Tw.CheckInput),
            Label.For("bind-subscribe").Class($"{Tw.CheckLabel} ms-1")["Subscribe to the newsletter"]
        ],
        Div.Class("mb-3")[
            Label.For("bind-age").Class($"{Tw.Label} text-sm")["Age"],
            Input.Bind(() => _model.Age)
                .Id("bind-age")
                .Class(Tw.Input)
                .Min("0")
                .Max("120")
        ],
        Div.Class("mb-3")[
            Label.For("bind-start").Class($"{Tw.Label} text-sm")["Start date"],
            Input.Bind(() => _model.StartDate)
                .Id("bind-start")
                .Class(Tw.Input)
        ],
        Div.Class("mb-3")[
            Label.For("bind-favorite").Class($"{Tw.Label} text-sm")["Favourite colour"],
            Select.Bind(() => _model.Favorite)
                .Id("bind-favorite")
                .Class(Tw.Select)[
                Option.Value("Red")["Red"],
                Option.Value("Green")["Green"],
                Option.Value("Blue")["Blue"]
            ]
        ],
        Pre.Class("text-sm mb-0 p-3 bg-slate-100 border rounded")[
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
