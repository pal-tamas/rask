using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

// First-error-wins: an inline per-field rule and a DataAnnotations rule both target the
// same field. EditContext gates later stages once any earlier stage has flagged the field,
// so the inline "Required." message appears while the input is empty, and ONLY after that
// rule passes does the [RegularExpression] format error surface.
public sealed partial class FirstErrorWinsDemo : Component
{
    private readonly LicenseModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Activated: {m.Code}").Class("flex flex-col gap-3")[
            Div[
                UiInput.Bind(() => _model.Code).Label("License code")
                    .Id("v8-code")
                    .Validate(v =>
                        string.IsNullOrWhiteSpace(v)
                            ? new[] { "Code is required." }
                            : Array.Empty<string>()).ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Code)
            ],
            Div[
                UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit)[UiIcon.Name(UiIconName.Unlock), "Activate"]
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle), _submission]
    ];
}

public sealed class LicenseModel
{
    [RegularExpression(@"^[A-Z]{3}-\d{3}$", ErrorMessage = "Use the ABC-123 format.")]
    public string Code { get; set; } = "";
}
