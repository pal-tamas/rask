using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class ValidationSummaryDemo : Component
{
    private static readonly (string? Value, string Text)[] Plans = [("free", "Free"), ("pro", "Pro"), ("team", "Team")];

    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        Ui.Alert.Tone(Ui.Tone.Error).Variant(Ui.Variant.Soft).Class("text-sm mb-0")[Div.Class("font-semibold mb-1")[
                Ui.Icon.Name(Ui.IconName.Warning).Class("me-1"),
                $"Please fix {entries.Count} error{(entries.Count == 1 ? "" : "s")}:"
            ], Ul.Class("mb-0 ps-3")[
                entries.Select((e, i) => Li.Key(i)[
                    e.Field.Length == 0
                        ? e.Message
                        : [Strong[e.Field], ": ", e.Message]
                ])
            ]];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Registered: {m.Name} <{m.Email}>").Class("flex flex-col gap-3")[
            ValidationSummary.Template(SummaryAlert),
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
                    .Options(Plans)
                    .Placeholder("— choose —")
                    .Label("Plan")
                    .Id("v2-plan")
                    .ShowValidation(false)
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
