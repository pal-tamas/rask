using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Rask.Data;

/// <summary>
///     The base of a value object that holds one text: its rules are declared where it is, and Rask generates the
///     rest — <c>public sealed partial record DriverName() : Text(MaxLength: 1000);</c>
/// </summary>
/// <remarks>
///     <para>
///         The generated half adds <c>DriverName.Create(value)</c>, which checks the rules and throws a
///         <see cref="Rask.Cqrs.RaskValidationException" /> naming the field, <c>TryCreate</c> and
///         <c>CreateOptional</c>, and the private constructor EF Core rebuilds a stored value through — so a row is
///         loaded as it was stored, never checked again. A form over a generated model asks the same rules under
///         the field that holds it, and the column is given <see cref="MaxLength" /> as its length.
///     </para>
///     <para>
///         Two values are equal when they are of the same type and hold the same text; the rules take no part.
///     </para>
/// </remarks>
public abstract record Text
{
    // Declared patterns are compile-time constants, so this holds one Regex per pattern an app declares.
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    private readonly int _maxLength;
    private readonly int _minLength;
    private readonly string? _pattern;
    private readonly string? _message;
    private readonly bool _trim;

    /// <summary>Declares the rules: <c>Text(MaxLength: 1000)</c>.</summary>
    /// <param name="MaxLength">The most characters it may hold, and the column's length; 0 for no limit.</param>
    /// <param name="MinLength">The fewest characters it must hold; 0 for none beyond being filled.</param>
    /// <param name="Pattern">A regular expression the text must match — anchor it (<c>^…$</c>) to cover the whole text — or null.</param>
    /// <param name="Message">What a text that does not match <paramref name="Pattern" /> is told, or null for the framework's.</param>
    /// <param name="Trim">Whether surrounding blanks are cut off before the text is checked and kept.</param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Text(MaxLength: 1000)`, like an attribute's named argument
    protected Text(int MaxLength = 0, int MinLength = 0, string? Pattern = null, string? Message = null, bool Trim = true)
#pragma warning restore IDE1006
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MinLength);
        if (MaxLength > 0)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MinLength, MaxLength);
        }

        _maxLength = MaxLength;
        _minLength = MinLength;
        _pattern = Pattern;
        _message = Message;
        _trim = Trim;
    }

    /// <summary>The text it holds.</summary>
    public string Value { get; protected init; } = "";

    /// <summary>The most characters it may hold, and its column's length; 0 for no limit.</summary>
    public int MaxLength => _maxLength;

    /// <summary>The fewest characters it must hold; 0 for none beyond being filled.</summary>
    public int MinLength => _minLength;

    /// <summary>The regular expression the text must match, or null.</summary>
    public string? Pattern => _pattern;

    /// <summary>What a text that does not match <see cref="Pattern" /> is told, or null for the framework's words.</summary>
    public string? Message => _message;

    /// <summary>Whether surrounding blanks are cut off before the text is checked and kept.</summary>
    public bool Trim => _trim;

    /// <summary>The same type, holding the same text.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>Whether the two are the same value.</returns>
    public virtual bool Equals(Text? other) =>
        ReferenceEquals(this, other) ||
        (other is not null && EqualityContract == other.EqualityContract && string.Equals(Value, other.Value, StringComparison.Ordinal));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EqualityContract, StringComparer.Ordinal.GetHashCode(Value));

    /// <summary>The text it holds.</summary>
    /// <returns><see cref="Value" />.</returns>
    public sealed override string ToString() => Value;

    /// <summary>What a given text is kept as: trimmed, unless the value object declares <c>Trim: false</c>.</summary>
    /// <param name="value">The text as given.</param>
    /// <returns>The text to check and keep.</returns>
    protected virtual string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _trim ? value.Trim() : value;
    }

    /// <summary>The rules <paramref name="value" /> breaks, after <see cref="Normalize" />. Asked by generated code.</summary>
    /// <param name="value">The text as given, or null for none.</param>
    /// <returns>Each rule it breaks; none when it may be kept.</returns>
    protected IEnumerable<ValueProblem> Problems(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [new ValueProblem(ValueProblemKind.Required)];
        }

        var text = Normalize(value);
        List<ValueProblem>? problems = null;

        if (text.Length < _minLength)
        {
            (problems ??= []).Add(new ValueProblem(ValueProblemKind.TooShort, _minLength));
        }

        if (_maxLength > 0 && text.Length > _maxLength)
        {
            (problems ??= []).Add(new ValueProblem(ValueProblemKind.TooLong, _maxLength));
        }

        if (_pattern is { } pattern && !Matches(pattern, text))
        {
            (problems ??= []).Add(new ValueProblem(ValueProblemKind.Pattern, null, _message));
        }

        return problems is null ? [] : problems;
    }

    private static bool Matches(string pattern, string text) =>
        Patterns.GetOrAdd(pattern, static p => new Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .IsMatch(text);
}
