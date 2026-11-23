using System.ComponentModel;
using System.Globalization;

namespace Rask.Data;

/// <summary>
///     One rule a value broke: which, and the limit it broke. <see cref="ValueWords" /> words it in the reader's
///     language; this carries no sentence of its own beyond the English that stands in for one.
/// </summary>
/// <remarks>
///     A base record cannot word its own messages: Rask's strings catalog lives in Rask.Core, which the server
///     build of Rask.Data does not reference. The app's generated module initializer hands the catalog to
///     <see cref="ValueWords" />, so the base says <i>what</i> was wrong and the catalog says it.
/// </remarks>
/// <param name="Kind">The rule that was broken.</param>
/// <param name="Limit">The limit it broke — the most characters, the least number, the latest day — or null.</param>
/// <param name="Message">The value object's own words for it (<c>Text(Message: …)</c>), which win over any.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct ValueProblem(ValueProblemKind Kind, object? Limit = null, string? Message = null)
{
    /// <summary>The framework's English for the rule, with <c>{0}</c> where the limit goes.</summary>
    public string Template =>
        Kind switch
        {
            ValueProblemKind.Required => GeneratedModelRules.RequiredMessage,
            ValueProblemKind.TooShort => "Enter at least {0} characters.",
            ValueProblemKind.TooLong => "Enter at most {0} characters.",
            ValueProblemKind.Pattern => "Enter a value in the expected format.",
            ValueProblemKind.BelowMin => "Enter {0} or more.",
            ValueProblemKind.AboveMax => "Enter {0} or less.",
            ValueProblemKind.TooManyDecimals => "Enter at most {0} decimal places.",
            ValueProblemKind.TooManyDigits => "The number is out of range.",
            ValueProblemKind.BeforeEarliest => "Choose a date on or after {0}.",
            ValueProblemKind.AfterLatest => "Choose a date on or before {0}.",
            ValueProblemKind.EndBeforeStart => "It cannot be before the start, {0}.",
            _ => "It must be after the start, {0}.",
        };

    /// <summary>What is said: the value object's own message, else the English with the limit written in.</summary>
    public string English => Message ?? string.Format(CultureInfo.CurrentCulture, Template, Limit);
}
