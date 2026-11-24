namespace Rask.Values;

/// <summary>
///     The base of a value object that holds a text of several lines:
///     <c>public sealed partial record Remark(string Value) : Multiline(Value, MaxLength: 4000);</c>
/// </summary>
/// <remarks>
///     A <see cref="Text" /> in every rule; a field bound to it — <c>Ui.Field.Bind(() =&gt; m.Remark)</c> — draws a
///     <c>Ui.Textarea</c>, as it does for a <c>[DataType(DataType.MultilineText)]</c> member.
/// </remarks>
public abstract record Multiline : Text
{
    /// <summary>Keeps <paramref name="Value" /> once the rules the other arguments declare accept it.</summary>
    /// <param name="Value">The text as given; the derived record's own positional parameter.</param>
    /// <param name="MaxLength">The most characters it may hold, and the column's length; 0 for no limit.</param>
    /// <param name="MinLength">The fewest characters it must hold; 0 for none beyond being filled.</param>
    /// <param name="Pattern">A regular expression the text must match, or null.</param>
    /// <param name="Message">What a text that does not match <paramref name="Pattern" /> is told, or null for the framework's.</param>
    /// <param name="Trim">Whether surrounding blanks (and blank lines) are cut off before it is checked and kept.</param>
    /// <param name="Upper">Whether it is kept in capitals.</param>
    /// <param name="Required">Whether it must be filled; with <c>false</c> an empty text is kept as <c>""</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the text.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Multiline(Value, MaxLength: 4000)`, like an attribute's named argument
    protected Multiline(
        string Value, int MaxLength = 0, int MinLength = 0, string? Pattern = null, string? Message = null, bool Trim = true, bool Upper = false,
        bool Required = true)
#pragma warning restore IDE1006
        : base(Value, new TextDeclared(MaxLength, MinLength, Pattern, Message, Trim, Upper, Required, TextKind.Multiline))
    {
    }
}
