namespace Rask.Example.Shared.Features;

// Select<T> in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the value in a field; OnChange writes it back and
//     re-renders this consumer, so the "Picked:" readout updates live (the controlled-OnChange fix).
//   • Bound — Select(() => model.X): two-way binds the model property through the ambient EditContext.
// Both readouts refresh on every change with no StateHasChanged.
public sealed partial class FormControlsSelectDemo : Component
{
    private string _controlled = "Rask";
    private readonly Model _model = new();

    protected override Component? Render() =>
        BsRow(Gutter: 4)[
            BsCol(Md: 6)[
                Label(Class: "form-label fw-semibold")["Controlled (Value + OnChange)"],
                Select<string>(
                    Value: _controlled,
                    OnChange: v => _controlled = v,
                    Class: "form-select mb-2",
                    Id: "fc-select-controlled")[
                    Option("Rask"), Option("Blazor"), Option("htmx")
                ],
                P(Class: "small text-secondary mb-0", Id: "fc-select-controlled-out")[
                    "Picked: ", Strong()[_controlled]
                ]
            ],
            BsCol(Md: 6)[
                Label(Class: "form-label fw-semibold")["Bound (two-way)"],
                Form(_model)[
                    Select(() => _model.Framework, Class: "form-select mb-2", Id: "fc-select-bound")[
                        Option("Rask"), Option("Blazor"), Option("htmx")
                    ]
                ],
                P(Class: "small text-secondary mb-0", Id: "fc-select-bound-out")[
                    "Picked: ", Strong()[_model.Framework]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Framework { get; set; } = "Rask";
    }
}
