namespace Rask.Site.Features;

// UiInput<T> — Rask.Core's Input<T> underneath — in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the text; OnChange fires on commit (blur/Enter) and
//     re-renders this consumer so the "Echo:" readout updates (the controlled-OnChange fix).
//   • Bound — Input.Bind(() => model.X): two-way binds and streams per keystroke through the EditContext.
public sealed partial class FormControlsInputDemo : Component
{
    private string _controlled = "";
    private readonly Model _model = new();

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("col-span-12 md:col-span-6")[
                UiInput.Value(_controlled).Label("Controlled (Value + OnChange)")
                    .OnChange(v => _controlled = v)
                    .Placeholder("Type, then blur…")
                    .Id("fc-input-controlled").Class("mb-2"),
                P.Class("text-sm text-ui-muted mb-0").Id("fc-input-controlled-out")[
                    "Echo: ", Strong[_controlled.Length == 0 ? "(empty)" : _controlled]
                ]
            ],
            Div.Class("col-span-12 md:col-span-6")[
                Form.Model(_model)[
                    UiInput.Bind(() => _model.Text).Label("Bound (two-way)")
                        .Placeholder("Type…")
                        .Id("fc-input-bound").Class("mb-2")
                ],
                P.Class("text-sm text-ui-muted mb-0").Id("fc-input-bound-out")[
                    "Echo: ", Strong[_model.Text.Length == 0 ? "(empty)" : _model.Text]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Text { get; set; } = "";
    }
}
