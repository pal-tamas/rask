namespace Rask.Example.Shared.Features;

// Input<T> in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the text; OnChange fires on commit (blur/Enter) and
//     re-renders this consumer so the "Echo:" readout updates (the controlled-OnChange fix).
//   • Bound — Input.Bind(() => model.X): two-way binds and streams per keystroke through the EditContext.
public sealed partial class FormControlsInputDemo : Component
{
    private string _controlled = "";
    private readonly Model _model = new();

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("md:col-span-6")[
                Label.Class($"{Ui.Label} font-semibold")["Controlled (Value + OnChange)"],
                Input
                    .Value(_controlled)
                    .OnChange(v => _controlled = v)
                    .Class($"{Ui.Input} mb-2")
                    .Placeholder("Type, then blur…")
                    .Id("fc-input-controlled"),
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fc-input-controlled-out")[
                    "Echo: ", Strong[_controlled.Length == 0 ? "(empty)" : _controlled]
                ]
            ],
            Div.Class("md:col-span-6")[
                Label.Class($"{Ui.Label} font-semibold")["Bound (two-way)"],
                Form.Model(_model)[
                    Input.Bind(() => _model.Text)
                        .Class($"{Ui.Input} mb-2")
                        .Placeholder("Type…")
                        .Id("fc-input-bound")
                ],
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fc-input-bound-out")[
                    "Echo: ", Strong[_model.Text.Length == 0 ? "(empty)" : _model.Text]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Text { get; set; } = "";
    }
}
