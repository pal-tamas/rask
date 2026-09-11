using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class InlineValidateDemo : Component
{
    private readonly LoginModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        // Filter to form-level entries — per-field rules already render through FieldError.
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return null;
        }

        return UiAlert.Tone(UiTone.Error).Variant(UiVariant.Soft).Class("text-sm mb-0")[Ul.Class("mb-0 ps-3")[
                formOnly.Select((e, i) => Li.Key(i)[e.Message])
            ]];
    }

    protected override Component? Render() =>
    [
        Form.Model(_model)
            .OnValidSubmit(m => _submission = $"Welcome, {m.Email}")
            .Class("flex flex-col gap-3")
            .Validate(m =>
                m.Password == m.Confirm ? Array.Empty<string>() : new[] { "Passwords do not match." })[
            Div[
                UiInput.Bind(() => _model.Email).Label("Email")
                    .Id("v4-email")
                    .Type(InputType.Email)
                    .Validate(v =>
                        v.Contains('@')
                            ? Array.Empty<string>()
                            : new[] { "Email looks wrong." }).ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Email)
            ],
            Div[
                UiInput.Bind(() => _model.Password).Label("Password").Id("v4-password").Type(InputType.Password)
            ],
            Div[
                UiInput.Bind(() => _model.Confirm).Label("Confirm").Id("v4-confirm").Type(InputType.Password)
            ],
            ValidationSummary.Template(SummaryAlert),
            Div[
                UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit)[UiIcon.Name(UiIconName.CheckCircle), "Sign in"]
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle), _submission]
    ];
}

public sealed class LoginModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Confirm { get; set; } = "";
}
