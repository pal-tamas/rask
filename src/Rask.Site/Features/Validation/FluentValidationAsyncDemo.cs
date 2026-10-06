using FluentValidation;

namespace Rask.Site.Features;

// FluentValidation async: a single RuleFor chain stacks NotEmpty → Matches → MustAsync.
// FluentValidationValidator wraps the whole IValidator into an IAsyncFieldValidator, so
// MustAsync awaits the network-shaped check, and the kit field shows "Checking…" while the
// await is in flight and the rule's message once it settles.
public sealed partial class FluentValidationAsyncDemo : Component
{
    private readonly TicketModel _model = new();
    private string? _submission;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Reserved: {m.Code}").Class("flex flex-col gap-3")[
            Ui.Input.Bind(() => _model.Code).Label("Ticket code").Id("v9-code"),
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.Ticket).Submit["Reserve"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Success.Soft.Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}
