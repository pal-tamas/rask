namespace Rask.Example.Shared.Features;

// Textarea<T> in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the text; OnChange fires on commit (blur) and
//     re-renders this consumer so the character-count readout updates (the controlled-OnChange fix).
//   • Bound — Textarea(() => model.X): two-way binds and streams per keystroke through the EditContext.
public sealed class FormControlsTextareaDemo : Component
{
    private string _controlled = "";
    private readonly Model _model = new();

    protected override RenderResult Render() =>
        Div(Class: "row g-4")[
            Div(Class: "col-md-6")[
                Label(Class: "form-label fw-semibold")["Controlled (Value + OnChange)"],
                Textarea<string>(
                    Value: _controlled,
                    OnChange: v => _controlled = v,
                    Class: "form-control mb-2",
                    Rows: 3,
                    Placeholder: "Type, then blur…",
                    Id: "fc-textarea-controlled"),
                P(Class: "small text-secondary mb-0", Id: "fc-textarea-controlled-out")[
                    "Length: ", Strong()[_controlled.Length.ToString()]
                ]
            ],
            Div(Class: "col-md-6")[
                Label(Class: "form-label fw-semibold")["Bound (two-way)"],
                Form(_model)[
                    Textarea(() => _model.Bio, Class: "form-control mb-2", Rows: 3, Placeholder: "Type…",
                        Id: "fc-textarea-bound")
                ],
                P(Class: "small text-secondary mb-0", Id: "fc-textarea-bound-out")[
                    "Length: ", Strong()[_model.Bio.Length.ToString()]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Bio { get; set; } = "";
    }
}
