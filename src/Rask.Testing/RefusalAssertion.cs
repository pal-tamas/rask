using Rask.Wire;

namespace Rask.Testing;

/// <summary>
///     A refusal <see cref="Test.Refuses(Action)" /> caught, and the sentences that say where it was refused:
///     <c>.At("Km.End")</c>, <c>.Only("Number")</c>, <c>.Whole()</c>. Each hands back the same refusal, so they chain.
/// </summary>
public sealed class RefusalAssertion
{
    internal RefusalAssertion(IReadOnlyList<FieldFailure> failures) => Failures = failures;

    /// <summary>Every failure the refusal named, in the order it named them.</summary>
    public IReadOnlyList<FieldFailure> Failures { get; }

    /// <summary>Asserts a failure is shown under <paramref name="field" />, among any others.</summary>
    /// <param name="field">The field's path as a form names it: <c>"Km.End"</c>, <c>"Parts[2].Quantity"</c>.</param>
    /// <returns>This refusal.</returns>
    /// <exception cref="RefusalAssertionException">None is.</exception>
    public RefusalAssertion At(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Failures.Any(f => f.Fields.Contains(field, StringComparer.Ordinal))
            ? this
            : throw new RefusalAssertionException($"Expected a refusal at {field}; refused {Found()}.");
    }

    /// <summary>Asserts every failure is shown under <paramref name="field" />, and under no other field.</summary>
    /// <param name="field">The field's path as a form names it: <c>"Number"</c>.</param>
    /// <returns>This refusal.</returns>
    /// <exception cref="RefusalAssertionException">A failure is shown elsewhere too.</exception>
    public RefusalAssertion Only(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Failures.All(f => f.Fields is [var only] && string.Equals(only, field, StringComparison.Ordinal))
            ? this
            : throw new RefusalAssertionException($"Expected a refusal at {field} alone; refused {Found()}.");
    }

    /// <summary>Asserts a failure is about the form as a whole, under no field.</summary>
    /// <returns>This refusal.</returns>
    /// <exception cref="RefusalAssertionException">Every failure names a field.</exception>
    public RefusalAssertion Whole() =>
        Failures.Any(static f => f.Fields.Count == 0)
            ? this
            : throw new RefusalAssertionException($"Expected a refusal of the whole form; refused {Found()}.");

    private string Found() => string.Join("; ", Failures.Select(static f => f.Fields.Count == 0
        ? $"the whole form (\"{f.Message}\")"
        : $"at {string.Join(", ", f.Fields)} (\"{f.Message}\")"));
}
