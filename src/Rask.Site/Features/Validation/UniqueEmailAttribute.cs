using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

// An async DataAnnotations rule (.NET 11): the lookup a real app sends to its database — here a short wait and a
// list, so the page runs anywhere. A form awaits IsValidAsync; nothing calls the synchronous IsValid, which only
// .NET's synchronous Validator would.
[AttributeUsage(AttributeTargets.Property)]
public sealed class UniqueEmailAttribute() : AsyncValidationAttribute("{0} is already registered.")
{
    private static readonly HashSet<string> Registered = new(StringComparer.OrdinalIgnoreCase) { "ada@rask.sh", "grace@rask.sh" };

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        throw new NotSupportedException("The registered emails are looked up asynchronously.");

    protected override async Task<ValidationResult?> IsValidAsync(
        object? value, ValidationContext validationContext, CancellationToken cancellationToken)
    {
        await Task.Delay(600, cancellationToken);

        // The lookup that fails: what a timed-out database says. The field reads "could not be completed", never valid.
        if (value is "down@rask.sh")
        {
            throw new TimeoutException("The user directory did not answer.");
        }

        return value is string email && Registered.Contains(email)
            ? new ValidationResult(FormatErrorMessage(email), [validationContext.MemberName!])
            : ValidationResult.Success;
    }
}
