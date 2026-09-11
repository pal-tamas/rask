using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class ValidationFieldsDemo : Component
{
    private static readonly (string? Value, string Text)[] Plans = [("free", "Free"), ("pro", "Pro"), ("team", "Team")];

    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("flex flex-col gap-3")[
            Div[
                UiInput.Bind(() => _model.Name).Label("Name").Id("v1-name").ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Name)
            ],
            Div[
                UiInput.Bind(() => _model.Email).Label("Email")
                    .Id("v1-email")
                    .Type(InputType.Email).ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Email)
            ],
            Div[
                UiInput.Bind(() => _model.Age).Label("Age").Id("v1-age").ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Age)
            ],
            Div[
                UiSelect.Bind(() => _model.Plan)
                    .Options(Plans)
                    .Placeholder("— choose —")
                    .Label("Plan")
                    .Id("v1-plan")
                    .ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Plan)
            ],
            Div[
                UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit)[UiIcon.Name(UiIconName.CheckCircle), "Register"]
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle), _submission]
    ];
}
