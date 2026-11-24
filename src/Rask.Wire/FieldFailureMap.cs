using Rask.Wire;

namespace Rask.Cqrs;

/// <summary>
///     Between the two shapes a rejected request has: the failures a form places under its fields, and the
///     <c>errors</c> dictionary that crosses the wire.
/// </summary>
/// <remarks>
///     The dictionary is the poorer of the two — a field name and its messages, nothing else — so going
///     through it loses which fields one failure named together, which were only marked, and what found it.
/// </remarks>
internal static class FieldFailureMap
{
    /// <summary>One failure per error, under the field the validator named — or under none.</summary>
    internal static IReadOnlyList<FieldFailure> FromErrors(IReadOnlyList<RequestValidationError> errors)
    {
        var failures = new List<FieldFailure>(errors.Count);
        foreach (var error in errors)
        {
            failures.Add(new FieldFailure(error.Message, FieldsOf(error.Field)));
        }

        return failures;
    }

    /// <summary>
    ///     The best the wire's dictionary allows: one failure per distinct message, under every field that
    ///     carries it — which puts a rule over several fields back together when its message is its own.
    /// </summary>
    internal static IReadOnlyList<FieldFailure> FromDictionary(IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return [];
        }

        var fieldsByMessage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                if (!fieldsByMessage.TryGetValue(message, out var fields))
                {
                    fieldsByMessage[message] = fields = [];
                    order.Add(message);
                }

                if (field.Length > 0)
                {
                    fields.Add(field);
                }
            }
        }

        return [.. order.Select(message => new FieldFailure(message, fieldsByMessage[message]))];
    }

    /// <summary>The wire shape: each message under every field its failure names, or under the empty key.</summary>
    internal static Dictionary<string, string[]> ToDictionary(IReadOnlyList<FieldFailure> failures)
    {
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var failure in failures)
        {
            foreach (var field in failure.Fields is { Count: > 0 } named ? named : [string.Empty])
            {
                if (!grouped.TryGetValue(field, out var messages))
                {
                    grouped[field] = messages = [];
                }

                messages.Add(failure.Message);
            }
        }

        return grouped.ToDictionary(static kv => kv.Key, static kv => kv.Value.ToArray(), StringComparer.Ordinal);
    }

    private static string[] FieldsOf(string? field) => string.IsNullOrEmpty(field) ? [] : [field];
}
