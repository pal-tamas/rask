namespace Rask.Example.Shared.Features;

// BsRadioGroup<TValue> — the single-value sibling of BsCheckboxGroup, here in controlled mode (Value + OnChange).
// Selecting an option calls OnChange (auto-wrapped), which re-renders this demo so the readout stays live.
public sealed class MultiSelectRadioDemo : Component
{
    private static readonly Tier[] AllTiers = [Tier.Free, Tier.Pro, Tier.Team];

    private Tier _plan = Tier.Free;

    protected override RenderResult Render() =>
        Div(Class: "vstack gap-3")[
            Div()[
                Label(Class: "form-label fw-semibold d-block")["Plan"],
                BsRadioGroup(
                    AllTiers,
                    Value: _plan,
                    OnChange: v => _plan = v,
                    ItemClass: "form-check-inline")
            ],
            P(Class: "small text-secondary mb-0", Id: "ms-radio-summary")[$"Plan: {_plan}"]
        ];

    private enum Tier
    {
        Free,
        Pro,
        Team
    }
}
