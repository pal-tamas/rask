namespace Rask.Example.Shared.Features;

// RadioGroup<TValue> — the single-value sibling of CheckboxGroup. Selecting an option writes the bound
// scalar property (here an enum) and re-validates the field.
public sealed class MultiSelectRadioDemo : Component
{
    private readonly Prefs _prefs = new();

    protected override RenderResult Render() =>
        Form(_prefs)[
            Div(Class: "mb-3")[
                Label(Class: "form-label fw-semibold d-block")["Plan"],
                RadioGroup(
                    () => _prefs.Plan,
                    [Tier.Free, Tier.Pro, Tier.Team],
                    p => Span(Class: "ms-1 me-3")[p.ToString()],
                    ItemClass: "form-check-label")
            ],
            P(Class: "small text-secondary mb-0", Id: "ms-radio-summary")[$"Plan: {_prefs.Plan}"]
        ];

    private enum Tier
    {
        Free,
        Pro,
        Team
    }

    private sealed class Prefs
    {
        public Tier Plan { get; set; } = Tier.Free;
    }
}
