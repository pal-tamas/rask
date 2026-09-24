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
        Form.Model(_model).OnSubmit(m => _submission = $"Activated: {m.Code}").Class("flex flex-col gap-3")[
            Div[
                Ui.Input.Bind(() => _model.Code).Label("License code")
                    .Id("v8-code")
                    .Validate(v =>
                        string.IsNullOrWhiteSpace(v)
                            ? new[] { "Code is required." }
                            : Array.Empty<string>()).ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Code)
            ],
            Div[
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.Unlock), "Activate"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}

public sealed class LicenseModel
{
    [RegularExpression(@"^[A-Z]{3}-\d{3}$", ErrorMessage = "Use the ABC-123 format.")]
    public string Code { get; set; } = "";
}
