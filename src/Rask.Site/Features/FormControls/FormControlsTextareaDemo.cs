namespace Rask.Site.Features;

// UiTextarea<T> — Rask.Core's Textarea<T> underneath — in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the text; OnChange fires on commit (blur) and
//     re-renders this consumer so the character-count readout updates (the controlled-OnChange fix).
//   • Bound — Textarea.Bind(() => model.X): two-way binds and streams per keystroke through the EditContext.
public sealed partial class FormControlsTextareaDemo : Component
{
    private string _controlled = "";
    private readonly Model _model = new();

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("col-span-12 md:col-span-6")[
                UiTextarea.Value(_controlled).Label("Controlled (Value + OnChange)")
                    .OnChange(v => _controlled = v)
                    .Rows(3)
                    .Hint("Type, then leave the field — OnChange fires on commit.")
                    .Id("fc-textarea-controlled").Class("mb-2"),
                P.Class("text-sm text-ui-muted mb-0").Id("fc-textarea-controlled-out")[
                    "Length: ", Strong[_controlled.Length.ToString()]
                ]
            ],
            Div.Class("col-span-12 md:col-span-6")[
                Form.Model(_model)[
                    UiTextarea.Bind(() => _model.Bio).Label("Bound (two-way)")
                        .Rows(3)
                        .Id("fc-textarea-bound").Class("mb-2")
                ],
                P.Class("text-sm text-ui-muted mb-0").Id("fc-textarea-bound-out")[
                    "Length: ", Strong[_model.Bio.Length.ToString()]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Bio { get; set; } = "";
    }
}
