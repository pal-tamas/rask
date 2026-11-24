namespace Rask.Values;

/// <summary>
///     The base of a value object that holds a web address:
///     <c>public sealed partial record Homepage(string Value) : Url(Value);</c>
/// </summary>
/// <remarks>
///     A <see cref="Text" /> that is an absolute <c>http</c> or <c>https</c> address — <c>[Url]</c> lets an <c>ftp</c>
///     one through as well, and anything that only starts like one — at most 2048 characters unless declared otherwise.
///     A control bound to it is an <c>&lt;input type="url"&gt;</c>, as one bound to a <c>[Url]</c> member is.
/// </remarks>
public abstract record Url : Text
{
    /// <summary>Keeps <paramref name="Value" /> once it is a web address the other arguments accept.</summary>
    /// <param name="Value">The address as given; the derived record's own positional parameter.</param>
    /// <param name="MaxLength">The most characters it may hold, and the column's length: 2048 unless declared.</param>
    /// <param name="MinLength">The fewest characters it must hold; 0 for none beyond being filled.</param>
    /// <param name="Message">What a text that is no http or https address is told, or null for the framework's.</param>
    /// <param name="Trim">Whether surrounding blanks are cut off before it is checked and kept.</param>
    /// <param name="Upper">Whether it is kept in capitals.</param>
    /// <param name="Required">Whether it must be filled; with <c>false</c> an empty address is kept as <c>""</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the address.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Url(Value, MaxLength: 500)`, like an attribute's named argument
    protected Url(
        string Value, int MaxLength = 2048, int MinLength = 0, string? Message = null, bool Trim = true, bool Upper = false, bool Required = true)
#pragma warning restore IDE1006
        : base(Value, new TextDeclared(MaxLength, MinLength, null, Message, Trim, Upper, Required, TextKind.Url))
    {
    }
}
