using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class ValidationSummaryDemo : Component
{
    private static readonly (string? Value, string Text)[] Plans = [("free", "Free"), ("pro", "Pro"), ("team", "Team")];

    private readonly RegistrationModel _model = new();
    private string? _submission;

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries) =>
        UiAlert.Tone(UiTone.Error).Variant(UiVariant.Soft).Class("text-sm mb-0")[Div.Class("font-semibold mb-1")[
                UiIcon.Name(UiIconName.Warning).Class("me-1"),
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
                UiInput.Bind(() => _model.Name).Label("Name").Id("v2-name").ShowValidation(false)
            ],
            Div[
                UiInput.Bind(() => _model.Email).Label("Email")
                    .Id("v2-email")
                    .Type(InputType.Email)
                    .ShowValidation(false)
            ],
            Div[
                UiInput.Bind(() => _model.Age).Label("Age").Id("v2-age").ShowValidation(false)
            ],
            Div[
                UiSelect.Bind(() => _model.Plan)
                    .Options(Plans)
                    .Placeholder("— choose —")
                    .Label("Plan")
                    .Id("v2-plan")
                    .ShowValidation(false)
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
