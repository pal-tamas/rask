namespace Rask.Site.Features;

public sealed partial class BindingClearDefaultDemo : Component
{
    private readonly Holder _model = new();

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            UiInput.Bind(() => _model.Age).Label("Age (non-nullable int) — clear → 0")
                .Id("bind-clear-age")
        ],
        Div.Class("mb-3")[
            UiInput.Bind(() => _model.OptionalAge).Label("Optional age (int?) — clear → null")
                .Id("bind-clear-optage")
                .Hint("Leave it empty for null.")
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
