namespace Rask.Values;

/// <summary>
///     The base of a value object that holds a phone number:
///     <c>public sealed partial record ContactPhone(string Value) : Phone(Value);</c>
/// </summary>
/// <remarks>
///     A <see cref="Text" /> checked as <c>[Phone]</c> checks it — digits, spaces, dashes, dots, brackets and a leading
///     <c>+</c>, an extension allowed — at most 32 characters unless declared otherwise. A control bound to it is an
///     <c>&lt;input type="tel" inputmode="tel" autocomplete="tel"&gt;</c>.
/// </remarks>
public abstract record Phone : Text
{
    /// <summary>Keeps <paramref name="Value" /> once it is a phone number the other arguments accept.</summary>
    /// <param name="Value">The number as given; the derived record's own positional parameter.</param>
    /// <param name="MaxLength">The most characters it may hold, and the column's length: 32 unless declared.</param>
    /// <param name="MinLength">The fewest characters it must hold; 0 for none beyond being filled.</param>
    /// <param name="Message">
    ///     What a number that breaks a rule declared here — its lengths, or being no phone number — is told, said once;
    ///     or null for the framework's words for each.
    /// </param>
    /// <param name="Trim">Whether surrounding blanks are cut off before it is checked and kept.</param>
    /// <param name="Upper">Whether it is kept in capitals.</param>
    /// <param name="Required">Whether it must be filled; with <c>false</c> an empty number is kept as <c>""</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the number.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Phone(Value, MaxLength: 20)`, like an attribute's named argument
    protected Phone(
        string Value, int MaxLength = 32, int MinLength = 0, string? Message = null, bool Trim = true, bool Upper = false, bool Required = true)
#pragma warning restore IDE1006
        : base(Value, new TextDeclared(MaxLength, MinLength, null, Message, Trim, Upper, Required, TextKind.Phone))
    {
    }
}
