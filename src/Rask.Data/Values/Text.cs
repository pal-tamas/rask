using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Rask.Data;

/// <summary>
///     The base of a value object that holds one text, with its rules declared where it is:
///     <c>public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000);</c>
/// </summary>
/// <remarks>
///     <para>
///         The constructor trims the text (unless <c>Trim: false</c>), puts it in capitals when it is declared
///         <c>Upper: true</c>, and checks it — filled, then the lengths and the
///         pattern — throwing a <see cref="Rask.Cqrs.RaskValidationException" /> when a rule refuses it. The generated
///         half adds <c>DriverName.Create(value, field)</c>, which shows a refusal under the field, <c>TryCreate</c>
///         and <c>CreateOptional</c>; a form over a generated model asks the same rules, a bound input is given
///         <c>MaxLength</c> as its <c>maxlength</c>, and the column is given it as its length.
///     </para>
///     <para>
///         <see cref="Value" /> is get-only, so <c>name with { Value = … }</c> does not compile. Two values are equal
///         when they are of the same type and hold the same text.
///     </para>
/// </remarks>
public abstract record Text : Value
{
    // Declared patterns are compile-time constants, so this holds one Regex per pattern an app declares.
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    /// <summary>Keeps <paramref name="Value" /> once the rules the other arguments declare accept it.</summary>
    /// <param name="Value">The text as given; the derived record's own positional parameter.</param>
    /// <param name="MaxLength">The most characters it may hold, and the column's length; 0 for no limit.</param>
    /// <param name="MinLength">The fewest characters it must hold; 0 for none beyond being filled.</param>
    /// <param name="Pattern">A regular expression the text must match — anchor it (<c>^…$</c>) to cover the whole text — or null.</param>
    /// <param name="Message">What a text that does not match <paramref name="Pattern" /> is told, or null for the framework's.</param>
    /// <param name="Trim">Whether surrounding blanks are cut off before the text is checked and kept.</param>
    /// <param name="Upper">
    ///     Whether the text is kept in capitals — <c>"wvwzzz1jzxw000001"</c> becomes <c>"WVWZZZ1JZXW000001"</c>, and
    ///     <c>"ő"</c> <c>"Ő"</c> — after it is trimmed and before it is checked, so the lengths and the pattern see it
    ///     as it is kept. Culture-invariant: the same text is kept on every machine.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the text.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Text(Value, MaxLength: 1000)`, like an attribute's named argument
    protected Text(string Value, int MaxLength = 0, int MinLength = 0, string? Pattern = null, string? Message = null, bool Trim = true, bool Upper = false)
#pragma warning restore IDE1006
        : base(checkNow: false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MinLength);
        if (MaxLength > 0)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MinLength, MaxLength);
        }

        if (DeclaredRules.IsReading)
        {
            DeclaredRules.Note(new TextRules(MaxLength, MinLength, Pattern));
        }

        if (Loading.IsOn)
        {
            this.Value = Value ?? "";
            return;
        }

        var given = Value ?? "";
        var trimmed = Trim ? given.Trim() : given;
        this.Value = Upper ? trimmed.ToUpperInvariant() : trimmed;
        Check(string.IsNullOrWhiteSpace(Value)
            ? [Say(ValueProblemKind.Required)]
            : Problems(this.Value, MaxLength, MinLength, Pattern, Message));
    }

    /// <summary>The text it holds.</summary>
    public string Value { get; }

    /// <summary>The text it holds.</summary>
    /// <returns><see cref="Value" />.</returns>
    public sealed override string ToString() => Value;

    private static List<Problem>? Problems(string text, int maxLength, int minLength, string? pattern, string? message)
    {
        List<Problem>? problems = null;

        if (text.Length < minLength)
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooShort, minLength));
        }

        if (maxLength > 0 && text.Length > maxLength)
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooLong, maxLength));
        }

        if (pattern is not null && !Matches(pattern, text))
        {
            (problems ??= []).Add(Say(ValueProblemKind.Pattern, null, message));
        }

        return problems;
    }

    private static Problem Say(ValueProblemKind kind, object? limit = null, string? message = null) =>
        Problem.Whole(ValueWords.Say(new ValueProblem(kind, limit, message)));

    private static bool Matches(string pattern, string text) =>
        Patterns.GetOrAdd(pattern, static p => new Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .IsMatch(text);
}
