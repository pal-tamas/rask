namespace Rask.Example.Shared.Features;

// The generic BsMultiSelect<TItem> bound to a model collection inside a Form. A per-field Validate rule —
// the same shape as Input's Validate — rejects fewer than two picks and surfaces its message inline through
// the control's own ValidationMessage. Live feedback (the chips and the validation message) lives inside the
// control, so it refreshes as you select without any StateHasChanged. (An inline Validate is used rather
// than a [MinLength] DataAnnotation so the WASM sample stays trim-clean.)
public sealed partial class MultiSelectDemo : Component
{
    private static readonly string[] AllInterests =
        ["Web", "Mobile", "AI", "Games", "DevOps", "Data"];

    private readonly Prefs _prefs = new();
    private string? _submission;

    protected override Component? Render() =>
    [
        Form(
            _prefs,
            OnValidSubmit: m => _submission = $"Saved {m.Interests.Count} interest(s)",
            Class: "vstack gap-3")[
            Div()[
                Label(Class: "form-label fw-semibold")["Interests"],
                BsMultiSelect<string>(
                    () => _prefs.Interests,
                    AllInterests,
                    Validate: interests => interests.Count >= 2
                        ? Array.Empty<string>()
                        : ["Pick at least two interests."],
                    Id: "ms-interests",
                    Placeholder: "Pick a few…")
            ],
            Div()[
                BsButton(Type: "submit", Color: BsColor.Primary)[BsIcon(Name: BsIconName.Check2Circle, Class: "me-1"), "Save"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _submission]
    ];

    private sealed class Prefs
    {
        public List<string> Interests { get; } = [];
    }
}
