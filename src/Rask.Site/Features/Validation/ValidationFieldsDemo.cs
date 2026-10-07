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
                    .Placeholder("— choose —")
                    .Label("Plan")
                    .Id("v1-plan")
                    .ShowValidation(false)[
                    Plans.Select(plan => Ui.SelectOption.Key(plan.Text).Value(plan.Value)[plan.Text])
                ],
                Validation.Message.Template(FieldError).For(() => _model.Plan)
            ],
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit["Register"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_submission)
    ];
}
