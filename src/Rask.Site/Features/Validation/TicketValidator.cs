using FluentValidation;

namespace Rask.Site.Features;

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
