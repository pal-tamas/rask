namespace Rask.Example.Shared.Features;

public sealed partial class BindingClearDefaultDemo : Component
{
    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            Label.For("bind-clear-age").Class($"{Tw.Label} text-sm")["Age (non-nullable int) — clear → 0"],
            Input.Bind(() => _model.Age)
                .Id("bind-clear-age")
                .Class(Tw.Input)
        ],
        Div.Class("mb-3")[
            Label.For("bind-clear-optage").Class($"{Tw.Label} text-sm")["Optional age (int?) — clear → null"],
            Input.Bind(() => _model.OptionalAge)
                .Id("bind-clear-optage")
                .Class(Tw.Input)
                .Placeholder("leave empty for null")
        ],
        Pre.Class("text-sm mb-0 p-3 bg-ui-well border rounded")[
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
