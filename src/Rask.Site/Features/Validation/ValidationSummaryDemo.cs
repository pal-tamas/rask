using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class ValidationSummaryDemo : Component
{
    private static readonly (string? Value, string Text)[] Plans = [("free", "Free"), ("pro", "Pro"), ("team", "Team")];

    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        Ui.Callout.Danger.Icon(Ui.IconName.ExclamationTriangle).Role("alert")
            .Heading($"Please fix {entries.Count} error{(entries.Count == 1 ? "" : "s")}:")[Ui.CalloutText[Ul.Class("mb-0 ps-3")[
                entries.Select((e, i) => Li.Key(i)[
                    e.Field.Length == 0
                        ? e.Message
                        : [Strong[e.Field], ": ", e.Message]
                ])
            ]]];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("flex flex-col gap-3")[
            Validation.Summary.Template(SummaryAlert),
            // ShowValidation(false) on every field: the errors belong to the summary above, which is what this
            // demo is showing, and a field that also said its own would say each one twice.
            Div[
                Ui.Input.Bind(() => _model.Name).Label("Name").Id("v2-name").ShowValidation(false)
            ],
            Div[
                Ui.Input.Bind(() => _model.Email).Label("Email")
                    .Id("v2-email")
                    .Type(InputType.Email)
                    .ShowValidation(false)
            ],
            Div[
                Ui.Input.Bind(() => _model.Age).Label("Age").Id("v2-age").ShowValidation(false)
            ],
            Div[
                Ui.Select.Bind(() => _model.Plan)
                    .Placeholder("— choose —")
                    .Label("Plan")
                    .Id("v2-plan")
                    .ShowValidation(false)[
                    Plans.Select(plan => Ui.SelectOption.Key(plan.Text).Value(plan.Value)[plan.Text])
                ]
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
