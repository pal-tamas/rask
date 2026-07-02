using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

// First-error-wins: an inline per-field rule and a DataAnnotations rule both target the
// same field. EditContext gates later stages once any earlier stage has flagged the field,
// so the inline "Required." message appears while the input is empty, and ONLY after that
// rule passes does the [RegularExpression] format error surface.
public sealed class FirstErrorWinsDemo : Component
{
    private readonly LicenseModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override Component? Render() =>
    [
        Form<LicenseModel>(
            _model,
            m => _submission = $"Activated: {m.Code}",
            Class: "vstack gap-3")[
            DataAnnotationsValidator(),
            Div()[
                Label("v8-code", Class: "form-label small mb-1")["License code"],
                Input(() => _model.Code, Id: "v8-code", Class: "form-control",
                    Validate: v =>
                        string.IsNullOrWhiteSpace(v)
                            ? new[] { "Code is required." }
                            : Array.Empty<string>()),
                ValidationMessage(() => _model.Code, FieldError)
            ],
            Div()[
                BsButton(Type: "submit", Color: BsColor.Primary)[BsIcon(Name: BsIconName.Unlock, Class: "me-1"), "Activate"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _submission]
    ];
}

public sealed class LicenseModel
{
    [RegularExpression(@"^[A-Z]{3}-\d{3}$", ErrorMessage = "Use the ABC-123 format.")]
    public string Code { get; set; } = "";
}
