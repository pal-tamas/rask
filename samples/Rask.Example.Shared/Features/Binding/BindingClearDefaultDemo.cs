namespace Rask.Example.Shared.Features;

public sealed partial class BindingClearDefaultDemo : Component
{
    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            Label.For("bind-clear-age").Class("form-label small")["Age (non-nullable int) — clear → 0"],
            Input(() => _model.Age)
                .Id("bind-clear-age")
                .Class("form-control")
        ],
        Div.Class("mb-3")[
            Label.For("bind-clear-optage").Class("form-label small")["Optional age (int?) — clear → null"],
            Input(() => _model.OptionalAge)
                .Id("bind-clear-optage")
                .Class("form-control")
                .Placeholder("leave empty for null")
        ],
        Pre.Class("small mb-0 p-3 bg-light border rounded")[
            Code.Id("bind-clear-echo")[
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
