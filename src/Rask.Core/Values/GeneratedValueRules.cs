using System.ComponentModel;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Values;

/// <summary>
///     What the generated half of a value object declared from <see cref="Value" /> or one of its bases is built
///     from: <c>Create</c>, <c>TryCreate</c>, <c>CreateOptional</c> and the <c>__Problems</c> a form asks all make
///     the value through its constructor, and read the answer here. Called by generated code; not an API to write
///     against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedValueRules
{
    /// <summary>Opens an attempt: a value refused while it is open is recorded instead of thrown.</summary>
    /// <returns>The attempt, to dispose once the value is made.</returns>
    public static ValueAttempt Attempt() => ValueAttempt.Begin();

    /// <summary>
    ///     Opens a loading scope: a value made while it is open is kept as given, its rules not asked — what a generated
    ///     wire codec rebuilds a value it read inside, as a row is loaded.
    /// </summary>
    /// <returns>The scope, to dispose on this thread once the value is made.</returns>
    public static ValueLoading Loading() => ValueLoading.Begin();

    /// <summary>The exception <c>X.Create(…, field)</c> throws for a value its rules refused.</summary>
    /// <param name="problems">What the rules said.</param>
    /// <param name="field">The field the value came from, as a form names it, put in front of each problem's own; or null.</param>
    /// <returns>The exception: a problem about the whole value under <paramref name="field" />, one about a member under <c>field.Member</c>.</returns>
    public static RaskValidationException Refused(IReadOnlyList<Problem> problems, string? field)
    {
        ArgumentNullException.ThrowIfNull(problems);

        return new RaskValidationException([.. problems.Select(problem => new FieldFailure(problem.Message, Path(field, problem.Field)))]);
    }

    /// <summary>The messages of <paramref name="problems" />, in order: what a form shows under the field.</summary>
    /// <param name="problems">What the rules said.</param>
    /// <returns>One message per problem.</returns>
    public static IReadOnlyList<string> Messages(IReadOnlyList<Problem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        return problems.Count == 0 ? [] : [.. problems.Select(static p => p.Message)];
    }

    /// <summary>The moment <paramref name="text" /> writes, on the wall clock of <see cref="Clock.Zone" />: what a <see cref="Moment" />'s <c>Parse</c> reads.</summary>
    /// <param name="text">The moment as text: ISO 8601 (with or without an offset), or as <paramref name="format" /> writes it.</param>
    /// <param name="format">The exact format it is written in, culture-invariant; null for ISO 8601.</param>
    /// <returns>The moment, of <see cref="DateTimeKind.Unspecified" />.</returns>
    /// <exception cref="FormatException"><paramref name="text" /> is no moment.</exception>
    public static DateTime ParseMoment(string text, string? format) =>
        WallText.TryMoment(text, format, out var moment) ? moment : throw WallText.NotA("moment", text, format);

    /// <summary>The moment <paramref name="text" /> writes, as <see cref="ParseMoment" /> reads it; false when it is none.</summary>
    /// <param name="text">The moment as text.</param>
    /// <param name="format">The exact format it is written in, culture-invariant; null for ISO 8601.</param>
    /// <param name="moment">The moment read; default when it is none.</param>
    /// <returns>Whether <paramref name="text" /> is a moment.</returns>
    public static bool TryParseMoment(string? text, string? format, out DateTime moment) => WallText.TryMoment(text, format, out moment);

    /// <summary>The day <paramref name="text" /> writes — a moment's day on the wall calendar, for a moment: what a <see cref="Day" />'s <c>Parse</c> reads.</summary>
    /// <param name="text">The day (or a moment) as text: ISO 8601, or as <paramref name="format" /> writes it.</param>
    /// <param name="format">The exact format it is written in, culture-invariant; null for ISO 8601.</param>
    /// <returns>The day.</returns>
    /// <exception cref="FormatException"><paramref name="text" /> is no day.</exception>
    public static DateOnly ParseDay(string text, string? format) =>
        WallText.TryDay(text, format, out var day) ? day : throw WallText.NotA("day", text, format);

    /// <summary>The day <paramref name="text" /> writes, as <see cref="ParseDay" /> reads it; false when it is none.</summary>
    /// <param name="text">The day as text.</param>
    /// <param name="format">The exact format it is written in, culture-invariant; null for ISO 8601.</param>
    /// <param name="day">The day read; default when it is none.</param>
    /// <returns>Whether <paramref name="text" /> is a day.</returns>
    public static bool TryParseDay(string? text, string? format, out DateOnly day) => WallText.TryDay(text, format, out day);

    /// <summary>What a value left empty is told, as the only message.</summary>
    /// <returns>The required message, worded like every other rule.</returns>
    public static IReadOnlyList<string> Required() => [ValueWords.Say(new ValueProblem(ValueProblemKind.Required))];

    /// <summary>The problem of a member <c>CreateOptional</c> was given nothing for while the others were filled.</summary>
    /// <param name="member">The member left empty.</param>
    /// <returns>The required message, under the member.</returns>
    public static Problem Missing(string member) => new(member, ValueWords.Say(new ValueProblem(ValueProblemKind.Required)));

    /// <summary>
    ///     Makes a value object that checks itself as it is made, under the field that holds it: a refusal about its
    ///     <c>End</c> becomes one about <c>Km.End</c>, where the form shows it.
    /// </summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="field">The member of the model that holds it.</param>
    /// <param name="make">Makes it.</param>
    /// <returns>What <paramref name="make" /> made.</returns>
    /// <exception cref="RaskValidationException">Its rules refused it; every failure named under <paramref name="field" />.</exception>
    public static T Under<T>(string field, Func<T> make)
    {
        ArgumentNullException.ThrowIfNull(make);

        try
        {
            return make();
        }
        catch (RaskValidationException refused)
        {
            throw new RaskValidationException(
                [.. refused.Failures.Select(f => new FieldFailure(f.Message, f.Fields.Count == 0 ? [field] : [.. f.Fields.Select(m => field + "." + m)]))],
                refused);
        }
    }

    private static string[] Path(string? field, string? member)
    {
        if (string.IsNullOrEmpty(member))
        {
            return string.IsNullOrEmpty(field) ? [] : [field];
        }

        return string.IsNullOrEmpty(field) ? [member] : [field + "." + member];
    }
}
