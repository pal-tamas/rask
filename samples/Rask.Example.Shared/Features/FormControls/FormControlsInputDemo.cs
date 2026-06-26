namespace Rask.Example.Shared.Features;

// Input<T> in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the text; OnChange fires on commit (blur/Enter) and
//     re-renders this consumer so the "Echo:" readout updates (the controlled-OnChange fix).
//   • Bound — Input(() => model.X): two-way binds and streams per keystroke through the EditContext.
public sealed class FormControlsInputDemo : Component
{
    private string _controlled = "";
    private readonly Model _model = new();

    protected override RenderResult Render() =>
        Div(Class: "row g-4")[
            Div(Class: "col-md-6")[
                Label(Class: "form-label fw-semibold")["Controlled (Value + OnChange)"],
                Input<string>(
                    Value: _controlled,
                    OnChange: v => _controlled = v,
                    Class: "form-control mb-2",
                    Placeholder: "Type, then blur…",
                    Id: "fc-input-controlled"),
                P(Class: "small text-secondary mb-0", Id: "fc-input-controlled-out")[
                    "Echo: ", Strong()[_controlled.Length == 0 ? "(empty)" : _controlled]
                ]
            ],
            Div(Class: "col-md-6")[
                Label(Class: "form-label fw-semibold")["Bound (two-way)"],
                Form(_model)[
                    Input(() => _model.Text, Class: "form-control mb-2", Placeholder: "Type…",
                        Id: "fc-input-bound")
                ],
                P(Class: "small text-secondary mb-0", Id: "fc-input-bound-out")[
                    "Echo: ", Strong()[_model.Text.Length == 0 ? "(empty)" : _model.Text]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Text { get; set; } = "";
    }
}
