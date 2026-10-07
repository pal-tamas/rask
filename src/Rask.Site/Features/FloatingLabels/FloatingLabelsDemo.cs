using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed partial class FloatingLabelsDemo : Component
{
    private static readonly (string? Value, string Text)[] Plans = [("free", "Free"), ("pro", "Pro"), ("team", "Team")];

    private readonly AccountModel _model = new();
    private string? _submission;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Created account for {m.FullName} <{m.Email}>").Class("flex flex-col gap-2")[
            // One line per field. A labelled kit text field floats its label by default: the caption sits in
            // the field until there is content, then rises out of the way. The label is the field's real
            // <label>, linked to the control, and each bound field shows its own validation message, fed by
            // the [Required]/[Range]/etc. attributes through the built-in DataAnnotations pass. Every property
            // is nullable — Rask clears to null.
            Ui.Input.Bind(() => _model.FullName).Label("Full name").Id("ff-FullName"),
            Ui.Input.Bind(() => _model.Email).Label("Email address").Type(InputType.Email).Id("ff-Email"),
            Ui.Input.Bind(() => _model.Age).Label("Age").Id("ff-Age"),
            Ui.Select.Bind(() => _model.Plan).Label("Plan").Placeholder("— choose —").Id("ff-Plan")[
                Plans.Select(plan => Ui.SelectOption.Key(plan.Text).Value(plan.Value)[plan.Text])
            ],
            Ui.Textarea.Bind(() => _model.Bio).Label("Short bio").Id("ff-Bio"),
            Div.Class("mt-1")[
                Ui.Button.Primary.Icon(Ui.IconName.UserPlus).Submit["Create account"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_submission)
    ];
}
