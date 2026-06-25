namespace Rask.Example.Shared.Features;

// RadioGroup (single value) + CheckboxGroup (collection) in controlled mode (Value + OnChange), with a live
// readout. The controls are Components; their OnChange callbacks are auto-wrapped (AutoCallback), so
// selecting an option re-renders this demo and the summary line updates immediately — no StateHasChanged.
public sealed class FormGroupsDemo : Component
{
    private static readonly Plan[] AllPlans = [Plan.Free, Plan.Pro, Plan.Team];
    private static readonly string[] AllInterests = ["Web", "Mobile", "AI", "Games"];

    private Plan _plan = Plan.Free;
    private ICollection<string> _interests = [];

    protected override RenderResult Render() =>
        Div(Class: "vstack gap-3")[
            Div()[
                Label(Class: "form-label fw-semibold d-block")["Plan"],
                RadioGroup(
                    AllPlans,
                    Value: _plan,
                    OnChange: v => _plan = v,
                    ItemClass: "form-check-inline")
            ],
            Div()[
                Label(Class: "form-label fw-semibold d-block")["Interests"],
                CheckboxGroup<string>(
                    AllInterests,
                    Value: _interests.ToList(),
                    OnChange: next => _interests = next,
                    ItemClass: "form-check-inline")
            ],
            P(Class: "small text-secondary mb-0", Id: "groups-summary")[
                $"Plan: {_plan} · Interests: "
                + (_interests.Count == 0 ? "none" : string.Join(", ", _interests))
            ]
        ];

    private enum Plan
    {
        Free,
        Pro,
        Team
    }
}
