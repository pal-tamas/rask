using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Rask.Wire;

namespace Rask.Core.Messaging;

// The app's own code saying no, in sentences written for the person at the screen: the field failures a
// store or a rule throws, or a DataAnnotations ValidationException. A form places what it can under its
// fields and in its own messages; what is left, and all of it from a handler outside a form, is one toast.
internal static class Refusal
{
    /// <summary>The failures <paramref name="thrown" /> refuses with, looking through the wrappers a call adds.</summary>
    // Null for anything else, and for an exception that names no failure: it has told the reader nothing.
    internal static IReadOnlyList<FieldFailure>? Find(Exception thrown) => thrown switch
    {
        IFieldFailures { Failures: { Count: > 0 } failures } => failures,

        // Exactly that type: a subclass is the app's own exception, and what it means is the app's to say.
        ValidationException refusal when refusal.GetType() == typeof(ValidationException) => [Failure(refusal)],
        AggregateException many => many.Flatten().InnerExceptions.Select(Find).FirstOrDefault(found => found is not null),
        TargetInvocationException { InnerException: { } inner } => Find(inner),
        _ => null,
    };

    /// <summary>Says <paramref name="failures" /> in one error toast, and whether a session drawing toasts took it.</summary>
    // One toast, each different sentence once and in order: the kit shows a single toast at a time, so a
    // toast per failure would leave only the last one to be read.
    internal static bool Say(IEnumerable<FieldFailure> failures) =>
        Toast.TryError(string.Join(' ', failures.Select(failure => failure.Message).Distinct(StringComparer.Ordinal)));

    /// <summary>What each failure came from, for the log: the rule or index it names, else its fields.</summary>
    internal static string Sources(IEnumerable<FieldFailure> failures) =>
        string.Join(", ", failures.Select(failure => failure.Source ?? string.Join('+', failure.Fields ?? [])));

    private static FieldFailure Failure(ValidationException refusal)
    {
        var result = refusal.ValidationResult;
        return new FieldFailure(
            result?.ErrorMessage ?? refusal.Message, [.. result?.MemberNames ?? []], Source: nameof(ValidationException));
    }
}
