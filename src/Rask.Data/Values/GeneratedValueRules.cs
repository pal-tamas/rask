using System.ComponentModel;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     What the generated half of a value object declared from a base record (<c>record DriverName() : Text(…)</c>)
///     is built from. Called by generated code; not an API to write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedValueRules
{
    /// <summary>The messages of <paramref name="problems" />, each worded by <paramref name="say" />.</summary>
    /// <param name="problems">The rules a value broke.</param>
    /// <param name="say">
    ///     Words one in the reader's language; null for the English. Never asked about a problem that carries the
    ///     value object's own message.
    /// </param>
    /// <returns>One message per problem, in order.</returns>
    public static IReadOnlyList<string> Said(IEnumerable<ValueProblem> problems, Func<ValueProblem, string>? say)
    {
        ArgumentNullException.ThrowIfNull(problems);

        List<string>? said = null;
        foreach (var problem in problems)
        {
            said ??= [];
            said.Add(problem.Message ?? (say is null ? problem.English : say(problem)));
        }

        return said is null ? [] : said;
    }

    /// <summary>The exception <c>X.Create</c> throws for a value its rules refused.</summary>
    /// <param name="messages">What the rules said.</param>
    /// <param name="field">The field the value came from, as a form names it; null for none.</param>
    /// <returns>The exception to throw, each message under <paramref name="field" />.</returns>
    public static RaskValidationException Refused(IReadOnlyList<string> messages, string? field)
    {
        ArgumentNullException.ThrowIfNull(messages);

        string[] fields = [field ?? ""];
        return new RaskValidationException([.. messages.Select(message => new FieldFailure(message, fields))]);
    }
}
