using System.ComponentModel.DataAnnotations;

namespace Rask.Data;

/// <summary>What one entity type is asked before a save writes it: see <see cref="GeneratedDomainRules" />.</summary>
/// <param name="Properties">Each property that carries a validation attribute.</param>
/// <param name="TypeChecks">The validation attributes on the type itself.</param>
/// <param name="Validate">Its <c>Validate()</c>, or null.</param>
/// <param name="ValidateDelete">Its <c>ValidateDelete()</c>, or null.</param>
/// <param name="Words">How a built-in attribute's failure is worded in the reader's language, or null for .NET's own.</param>
internal sealed record DomainRules(
    IReadOnlyList<CheckedProperty> Properties,
    IReadOnlyList<ValidationAttribute> TypeChecks,
    Func<object, IAsyncEnumerable<Problem>>? Validate,
    Func<object, IAsyncEnumerable<Problem>>? ValidateDelete,
    Func<ValidationAttribute, object?, string, string>? Words)
{
    /// <summary>What <paramref name="check" /> failing on <paramref name="value" /> says, having said <paramref name="said" />.</summary>
    internal string Worded(ValidationAttribute check, object? value, string said) => Words?.Invoke(check, value, said) ?? said;
}
