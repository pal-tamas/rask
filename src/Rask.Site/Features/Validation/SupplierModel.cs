using FluentValidation;

namespace Rask.Site.Features;

public sealed class SupplierModel
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public int? Rating { get; set; }

    // The rules live in the type, like its Configure: found at compile time, no validator class to write.
    internal static void Rules(InlineValidator<SupplierModel> rules)
    {
        rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(40);
        rules.RuleFor(s => s.Email).EmailAddress().WithMessage("That is not an e-mail address.");
        rules.RuleFor(s => s.Rating).InclusiveBetween(1, 5);
    }
}
