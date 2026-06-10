namespace Rask.Example.Shared.Demos;

// RadioGroup (single value) + CheckboxGroup (collection) bound to a model, with a live readout.
// Changing any option re-renders this demo (the change handlers' owner resolves to it), so the
// summary line updates immediately.
public sealed class FormGroupsDemo : Component
{
    private readonly Prefs _prefs = new();

    protected override RenderResult Render() =>
        Form(_prefs)[
            Div(Class: "mb-3")[
                Label(Class: "form-label fw-semibold d-block")["Plan"],
                RadioGroup(
                    () => _prefs.Plan,
                    new[] { Plan.Free, Plan.Pro, Plan.Team },
                    p => Span(Class: "ms-1 me-3")[p.ToString()],
                    ItemClass: "form-check-label")
            ],
            Div(Class: "mb-3")[
                Label(Class: "form-label fw-semibold d-block")["Interests"],
                CheckboxGroup<string>(
                    () => _prefs.Interests,
                    new[] { "Web", "Mobile", "AI", "Games" },
                    t => Span(Class: "ms-1 me-3")[t],
                    ItemClass: "form-check-label")
            ],
            P(Class: "small text-secondary mb-0", Id: "groups-summary")[
                $"Plan: {_prefs.Plan} · Interests: "
                + (_prefs.Interests.Count == 0 ? "none" : string.Join(", ", _prefs.Interests))
            ]
        ];

    private enum Plan
    {
        Free,
        Pro,
        Team
    }

    private sealed class Prefs
    {
        public Plan Plan { get; set; } = Plan.Free;
        public List<string> Interests { get; } = new();
    }
}
