namespace Rask.Values;

/// <summary>
///     The base of a value object that holds an email address:
///     <c>public sealed partial record ContactEmail(string Value) : Email(Value, MaxLength: 200);</c>
/// </summary>
/// <remarks>
///     A <see cref="Text" /> checked as <c>[EmailAddress]</c> checks it — one <c>@</c>, neither first nor last — at
///     most 254 characters unless declared otherwise. A control bound to it is an <c>&lt;input type="email"
///     autocomplete="email"&gt;</c>, exactly as one bound to an <c>[EmailAddress]</c> member is.
/// </remarks>
public abstract record Email : Text
{
    /// <summary>Keeps <paramref name="Value" /> once it is an email address the other arguments accept.</summary>
    /// <param name="Value">The address as given; the derived record's own positional parameter.</param>
    /// <param name="MaxLength">The most characters it may hold, and the column's length: 254, the most an address may be.</param>
    /// <param name="MinLength">The fewest characters it must hold; 0 for none beyond being filled.</param>
    /// <param name="Message">What a text that is no email address is told, or null for the framework's.</param>
    /// <param name="Trim">Whether surrounding blanks are cut off before it is checked and kept.</param>
    /// <param name="Upper">Whether it is kept in capitals.</param>
    /// <param name="Required">Whether it must be filled; with <c>false</c> an empty address is kept as <c>""</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the address.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Email(Value, MaxLength: 200)`, like an attribute's named argument
    protected Email(
        string Value, int MaxLength = 254, int MinLength = 0, string? Message = null, bool Trim = true, bool Upper = false, bool Required = true)
#pragma warning restore IDE1006
        : base(Value, new TextDeclared(MaxLength, MinLength, null, Message, Trim, Upper, Required, TextKind.Email))
    {
    }
}
