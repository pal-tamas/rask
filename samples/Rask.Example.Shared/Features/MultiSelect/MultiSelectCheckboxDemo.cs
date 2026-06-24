namespace Rask.Example.Shared.Features;

// CheckboxGroup<TItem> — the framework primitive for selecting many values into an ICollection. Each
// option is a checkbox; toggling adds/removes from the bound collection and re-validates the field.
public sealed class MultiSelectCheckboxDemo : Component
{
    private readonly Prefs _prefs = new();

    protected override RenderResult Render() =>
        Form(_prefs)[
            Div(Class: "mb-3")[
                Label(Class: "form-label fw-semibold d-block")["Interests"],
                CheckboxGroup<string>(
                    () => _prefs.Interests,
                    ["Web", "Mobile", "AI", "Games"],
                    ItemClass: "form-check-inline")
            ],
            P(Class: "small text-secondary mb-0", Id: "ms-checkbox-summary")[
                "Selected: " + (_prefs.Interests.Count == 0 ? "none" : string.Join(", ", _prefs.Interests))
            ]
        ];

    private sealed class Prefs
    {
        public List<string> Interests { get; } = [];
    }
}
