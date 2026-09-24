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
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.Ticket), "Reserve"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}

public sealed class TicketModel
{
    public string Code { get; set; } = "";
}

// CascadeMode.Stop keeps FV's own chain aligned with Rask's first-error-wins gating:
// NotEmpty must pass before Matches runs, which must pass before MustAsync fires.
public sealed class TicketValidator : AbstractValidator<TicketModel>
{
    private static readonly HashSet<string> Used = new(StringComparer.OrdinalIgnoreCase)
    {
        "TKT-001", "TKT-002", "TKT-003"
    };

    public TicketValidator()
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Code is required.")
            .Matches(@"^TKT-\d{3}$").WithMessage("Format must be TKT-123.")
            .MustAsync(async (code, ct) =>
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
                return !Used.Contains(code);
            }).WithMessage("Code is already reserved.");
    }
}
