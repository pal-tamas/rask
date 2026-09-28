using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Site.Features;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MatchesPropertyAttribute(string otherProperty) : ValidationAttribute
{
    public string OtherProperty { get; } = otherProperty;

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification =
            "GetProperty on the model's runtime type — the model is preserved by the user's binding setup, same contract as the validator itself.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var instance = validationContext.ObjectInstance;
        var sibling = instance.GetType().GetProperty(OtherProperty);
        if (sibling is null)
        {
            return new ValidationResult($"Unknown property '{OtherProperty}'.");
        }

        var other = sibling.GetValue(instance);
        string[]? members = validationContext.MemberName is null ? null : [validationContext.MemberName];
        return Equals(value, other)
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage ?? $"Must match {OtherProperty}.", members);
    }
}
