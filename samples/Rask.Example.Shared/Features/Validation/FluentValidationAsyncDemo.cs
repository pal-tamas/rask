using FluentValidation;

namespace Rask.Example.Shared.Features;

// FluentValidation async: a single RuleFor chain stacks NotEmpty → Matches → MustAsync.
// FluentValidationValidator wraps the whole IValidator into an IAsyncFieldValidator, so
// MustAsync awaits the network-shaped check and the ValidatingIndicator surfaces while
// the await is in flight.
public sealed partial class FluentValidationAsyncDemo : Component
{
    private readonly TicketModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    private static Component Checking() =>
        Span.Class("validating-indicator text-ui-muted text-sm mt-1")[
            UiIcon.Name(UiIconName.Retry).Class("me-1"), "Checking availability..."
        ];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Reserved: {m.Code}").Class("flex flex-col gap-3")[
            FluentValidationValidator.Validator(new TicketValidator()),
            Div[
                Label.For("v9-code").Class($"{Tw.Label} text-sm mb-1")["Ticket code"],
                Input.Bind(() => _model.Code).Id("v9-code").Class(Tw.Input),
                ValidatingIndicator.Template(Checking).For(() => _model.Code),
                ValidationMessage.Template(FieldError).For(() => _model.Code)
            ],
            Div[
                Button.Class(Tw.BtnPrimary).Type("submit")[UiIcon.Name(UiIconName.Ticket).Class("me-1"), "Reserve"]
            ]
        ],
        _submission is null
            ? null
            : Div.Role("status").Class($"{Tw.AlertSuccess} text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle).Class("me-2"), _submission]
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
