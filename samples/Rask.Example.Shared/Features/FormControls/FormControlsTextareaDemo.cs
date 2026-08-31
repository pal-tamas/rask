namespace Rask.Example.Shared.Features;

// Textarea<T> in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the text; OnChange fires on commit (blur) and
//     re-renders this consumer so the character-count readout updates (the controlled-OnChange fix).
//   • Bound — Textarea.Bind(() => model.X): two-way binds and streams per keystroke through the EditContext.
public sealed partial class FormControlsTextareaDemo : Component
{
    private string _controlled = "";
    private readonly Model _model = new();

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("md:col-span-6")[
                Label.Class($"{Ui.Label} font-semibold")["Controlled (Value + OnChange)"],
                Textarea
                    .Value(_controlled)
                    .OnChange(v => _controlled = v)
                    .Class($"{Ui.Input} mb-2")
                    .Rows(3)
                    .Placeholder("Type, then blur…")
                    .Id("fc-textarea-controlled"),
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fc-textarea-controlled-out")[
                    "Length: ", Strong[_controlled.Length.ToString()]
                ]
            ],
            Div.Class("md:col-span-6")[
                Label.Class($"{Ui.Label} font-semibold")["Bound (two-way)"],
                Form.Model(_model)[
                    Textarea.Bind(() => _model.Bio)
                        .Class($"{Ui.Input} mb-2")
                        .Rows(3)
                        .Placeholder("Type…")
                        .Id("fc-textarea-bound")
                ],
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fc-textarea-bound-out")[
                    "Length: ", Strong[_model.Bio.Length.ToString()]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Bio { get; set; } = "";
    }
}
