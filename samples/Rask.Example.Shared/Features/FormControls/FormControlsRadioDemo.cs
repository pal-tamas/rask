namespace Rask.Example.Shared.Features;

// BsRadioGroup<TValue> (example control, single value) in both shapes side by side.
//   • Controlled — Options + Value + OnChange: the parent owns the value; OnChange (auto-wrapped) re-renders
//     this consumer so the "Plan:" readout updates immediately.
//   • Bound — BsRadioGroup(() => model.X, options): two-way binds the scalar through the EditContext.
public sealed class FormControlsRadioDemo : Component
{
    private static readonly Plan[] AllPlans = [Plan.Free, Plan.Pro, Plan.Team];

    private Plan _controlled = Plan.Free;
    private readonly Model _model = new();

    protected override Component? Render() =>
        Div(Class: "row g-4")[
            Div(Class: "col-md-6", Id: "fc-radio-controlled")[
                Label(Class: "form-label fw-semibold d-block")["Controlled (Value + OnChange)"],
                BsRadioGroup(
                    AllPlans,
                    Value: _controlled,
                    OnChange: v => _controlled = v,
                    Name: "fc-radio-c",
                    ItemClass: "form-check-inline"),
                P(Class: "small text-secondary mb-0", Id: "fc-radio-controlled-out")[
                    "Plan: ", Strong()[_controlled.ToString()]
                ]
            ],
            Div(Class: "col-md-6", Id: "fc-radio-bound")[
                Label(Class: "form-label fw-semibold d-block")["Bound (two-way)"],
                Form(_model)[
                    // Label: names the group — the options render inside a <fieldset>/<legend> for the
                    // correct accessible grouping semantics.
                    BsRadioGroup(() => _model.Plan, AllPlans, Name: "fc-radio-b", Label: "Plan",
                        ItemClass: "form-check-inline")
                ],
                P(Class: "small text-secondary mb-0", Id: "fc-radio-bound-out")[
                    "Plan: ", Strong()[_model.Plan.ToString()]
                ]
            ]
        ];

    private enum Plan
    {
        Free,
        Pro,
        Team
    }

    private sealed class Model
    {
        public Plan Plan { get; set; } = Plan.Free;
    }
}
