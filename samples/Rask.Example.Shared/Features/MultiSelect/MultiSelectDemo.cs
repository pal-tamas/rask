using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

// The generic MultiSelect<TItem> bound to a model collection inside a Form: selecting/removing options
// mutates model.Interests and a form-level Validate rule rejects fewer than two picks. OnChange =
// StateHasChanged so the live summary re-renders as selections change. (A form-level inline validator
// is used rather than a [MinLength] DataAnnotation so the WASM sample stays trim-clean.)
public sealed class MultiSelectDemo : Component
{
    private static readonly string[] AllInterests =
        ["Web", "Mobile", "AI", "Games", "DevOps", "Data"];

    private readonly Prefs _prefs = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        return formOnly.Count == 0
            ? Fragment()
            : Div(Class: "alert alert-danger small mb-0")[
                Ul(Class: "mb-0 ps-3")[formOnly.Select((e, i) => Li(Key: i)[e.Message])]
            ];
    }

    protected override RenderResult Render() =>
    [
        Form(
            _prefs,
            OnValidSubmit: m => _submission = $"Saved {m.Interests.Count} interest(s)",
            Class: "vstack gap-3",
            Validate: m =>
                m.Interests.Count >= 2 ? Array.Empty<string>() : new[] { "Pick at least two interests." })[
            Div()[
                Label(Class: "form-label fw-semibold")["Interests"],
                MultiSelect<string>(
                    () => _prefs.Interests,
                    AllInterests,
                    Id: "ms-interests",
                    Placeholder: "Pick a few…",
                    OnChange: StateHasChanged)
            ],
            P(Class: "small text-secondary mb-0", Id: "ms-summary")[
                "Selected: " + (_prefs.Interests.Count == 0 ? "none" : string.Join(", ", _prefs.Interests))
            ],
            ValidationSummary(SummaryAlert),
            Div()[
                Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Save"]
            ]
        ],
        _submission is null
            ? Fragment()
            : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];

    private sealed class Prefs
    {
        public List<string> Interests { get; } = [];
    }
}
