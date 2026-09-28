using FluentValidation;

namespace Rask.Site.Features;

public sealed class NestedOrderAddressValidator : AbstractValidator<NestedOrderAddress>
{
    public NestedOrderAddressValidator()
    {
        RuleFor(x => x.Street).NotEmpty().WithMessage("Street is required.");
        RuleFor(x => x.City).NotEmpty().WithMessage("City is required.");
    }
}
