using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Rask.Values;

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
    /// <param name="Message">
    ///     What a text that breaks a rule declared here — too short, too long, not matching <paramref name="Pattern" /> —
    ///     is told, said once however many it breaks; or null for the framework's words for each. A text left blank is
    ///     still told it is required.
    /// </param>
    /// <param name="Trim">Whether surrounding blanks are cut off before the text is checked and kept.</param>
    /// <param name="Upper">
    ///     Whether the text is kept in capitals — <c>"wvwzzz1jzxw000001"</c> becomes <c>"WVWZZZ1JZXW000001"</c>, and
    ///     <c>"ő"</c> <c>"Ő"</c> — after it is trimmed and before it is checked, so the lengths and the pattern see it
    ///     as it is kept. Culture-invariant: the same text is kept on every machine.
    /// </param>
    /// <param name="Required">
    ///     Whether the text must be filled. With <c>false</c> an empty text (or only blanks) is a value of its own,
    ///     kept as <c>""</c> and asked only its own <c>Rules()</c>; the lengths and the pattern are checked for a filled one.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the text.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Text(Value, MaxLength: 1000)`, like an attribute's named argument
    protected Text(
        string Value, int MaxLength = 0, int MinLength = 0, string? Pattern = null, string? Message = null, bool Trim = true, bool Upper = false,
        bool Required = true)
#pragma warning restore IDE1006
        : this(Value, new TextDeclared(MaxLength, MinLength, Pattern, Message, Trim, Upper, Required))
    {
    }

    // What the bases of one kind of text (Email, Phone, Url, Password, Multiline) hand on: Text's rules and the kind,
    // which adds its own format check and tells a bound input what it is typed into.
    private protected Text(string value, TextDeclared declared)
        : base(checkNow: false)
    {
        if (declared.MaxLength < 0 || declared.MinLength < 0 || (declared.MaxLength > 0 && declared.MinLength > declared.MaxLength))
        {
            throw new ArgumentOutOfRangeException(
                nameof(declared), declared, "A length is never negative, and the fewest characters never exceed the most.");
        }

        if (DeclaredRules.IsReading)
        {
            DeclaredRules.Note(new TextRules(declared.MaxLength, declared.MinLength, declared.Pattern, declared.Required, declared.Kind));
        }

        if (RowLoading.IsOn)
        {
            Value = value ?? "";
            return;
        }

        var given = value ?? "";
        var trimmed = declared.Trim ? given.Trim() : given;
        Value = declared.Upper ? trimmed.ToUpperInvariant() : trimmed;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!declared.Required)
            {
                Value = "";
            }

            Check(declared.Required ? [Say(ValueProblemKind.Required)] : null);
            return;
        }

        Check(Problems(Value, declared));
    }

    /// <summary>The text it holds.</summary>
    public string Value { get; }

    /// <summary>The text it holds — a <see cref="Password" />'s masked, so a log line or an interpolation never shows it.</summary>
    /// <returns><see cref="Value" />, or <c>"••••"</c> for a password.</returns>
    public sealed override string ToString() => Shown();

    // What ToString says: the text, or the mask a password puts over it.
    private protected virtual string Shown() => Value;

    private static List<Problem>? Problems(string text, TextDeclared declared)
    {
        List<Problem>? problems = null;

        if (text.Length < declared.MinLength)
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooShort, declared.MinLength, declared.Message));
        }

        if (declared.MaxLength > 0 && text.Length > declared.MaxLength)
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooLong, declared.MaxLength, declared.Message));
        }

        if (declared.Pattern is { } pattern && !Matches(pattern, text))
        {
            (problems ??= []).Add(Say(ValueProblemKind.Pattern, null, declared.Message));
        }

        if (TextFormats.Refusal(declared.Kind, text) is { } format)
        {
            (problems ??= []).Add(Say(format, null, declared.Message));
        }

        return ValueWords.Once(problems, declared.Message);
    }

    private static Problem Say(ValueProblemKind kind, object? limit = null, string? message = null) =>
        Problem.Whole(ValueWords.Say(new ValueProblem(kind, limit, message)));

    private static bool Matches(string pattern, string text) =>
        Patterns.GetOrAdd(pattern, static p => new Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .IsMatch(text);
}
