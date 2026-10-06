using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

// Custom ValidationAttribute showcase. Three flavors flow through the built-in pass
// unchanged because System.ComponentModel.DataAnnotations.Validator walks every attribute on the
// property — there's no opt-in needed for user-authored subclasses:
//   • StrongPassword overrides IsValid(object?) — the simplest shape.
//   • MatchesProperty overrides GetValidationResult(object?, ValidationContext) — uses
//     ValidationContext.ObjectInstance to do cross-field comparison.
//   • NotBanned overrides GetValidationResult and resolves IBannedWordService via
//     ValidationContext.GetService<T>() — proves the render-scoped IServiceProvider flows through.
public sealed partial class CustomAttributeDemo : Component
{
    private readonly CustomAttributeModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Welcome, {m.Username}!").Class("flex flex-col gap-3")[
            Div[
                Ui.Input.Bind(() => _model.Username).Label("Username").Id("v12-username").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Username)
            ],
            Div[
                Ui.Input.Bind(() => _model.Password).Label("Password").Id("v12-password").Type(InputType.Password).ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Password)
            ],
            Div[
                Ui.Input.Bind(() => _model.ConfirmPassword).Label("Confirm password").Id("v12-confirm").Type(InputType.Password).ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.ConfirmPassword)
            ],
            Div[
                Ui.Button.Primary.Submit[Ui.Icon.Name(Ui.IconName.ShieldCheck), "Create account"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Success.Soft.Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}
