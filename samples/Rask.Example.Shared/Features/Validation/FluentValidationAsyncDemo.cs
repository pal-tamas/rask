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
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span.Class("validating-indicator text-muted small mt-1")[
            BsIcon.Name(BsIconName.ArrowClockwise).Class("me-1"), "Checking availability..."
        ];

    protected override Component? Render() =>
    [
        Form<TicketModel>(
            _model,
            m => _submission = $"Reserved: {m.Code}",
            Class: "vstack gap-3")[
            FluentValidationValidator.Validator(new TicketValidator()),
            Div[
                Label.For("v9-code").Class("form-label small mb-1")["Ticket code"],
                Input(() => _model.Code).Id("v9-code").Class("form-control"),
                ValidatingIndicator(() => _model.Code, Checking),
                ValidationMessage(() => _model.Code, FieldError)
            ],
            Div[
                BsButton.Type("submit").Color(BsColor.Primary)[BsIcon.Name(BsIconName.TicketPerforated).Class("me-1"), "Reserve"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert.Color(BsColor.Success).Class("small mt-3 mb-0")[BsIcon.Name(BsIconName.CheckCircle).Class("me-2"), _submission]
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
