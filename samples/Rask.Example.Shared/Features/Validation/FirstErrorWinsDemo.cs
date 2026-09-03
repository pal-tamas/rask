using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

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
                Label.For("v8-code").Class($"{Ui.Label} text-sm mb-1")["License code"],
                Input.Bind(() => _model.Code)
                    .Id("v8-code")
                    .Class(Ui.Input)
                    .Validate(v =>
                        string.IsNullOrWhiteSpace(v)
                            ? new[] { "Code is required." }
                            : Array.Empty<string>()),
                ValidationMessage.Template(FieldError).For(() => _model.Code)
            ],
            Div[
                Button.Class(Ui.BtnPrimary).Type("submit")[Icon.Name(IconName.Unlock).Class("me-1"), "Activate"]
            ]
        ],
        _submission is null
            ? null
            : Div.Role("status").Class($"{Ui.AlertSuccess} text-sm mt-3 mb-0")[Icon.Name(IconName.CheckCircle).Class("me-2"), _submission]
    ];
}

public sealed class LicenseModel
{
    [RegularExpression(@"^[A-Z]{3}-\d{3}$", ErrorMessage = "Use the ABC-123 format.")]
    public string Code { get; set; } = "";
}
