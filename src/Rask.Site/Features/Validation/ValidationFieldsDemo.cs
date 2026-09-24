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
        Form.Model(_model).OnSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("flex flex-col gap-3")[
            Div[
                Ui.Input.Bind(() => _model.Name).Label("Name").Id("v1-name").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Name)
            ],
            Div[
                Ui.Input.Bind(() => _model.Email).Label("Email")
                    .Id("v1-email")
                    .Type(InputType.Email).ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Email)
            ],
            Div[
                Ui.Input.Bind(() => _model.Age).Label("Age").Id("v1-age").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Age)
            ],
            Div[
                Ui.Select.Bind(() => _model.Plan)
                    .Options(Plans)
                    .Placeholder("— choose —")
                    .Label("Plan")
                    .Id("v1-plan")
                    .ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Plan)
            ],
            Div[
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.CheckCircle), "Register"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}
