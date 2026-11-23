using System.ComponentModel;

namespace Rask.Data;

/// <summary>Which rule of a value object's base record a value broke: what <see cref="ValueWords" /> words.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum ValueProblemKind
{
    /// <summary>Nothing was given, or only blanks.</summary>
    Required,

    /// <summary>A text shorter than its fewest characters, the <see cref="ValueProblem.Limit" />.</summary>
    TooShort,

    /// <summary>A text longer than its most characters, the <see cref="ValueProblem.Limit" />.</summary>
    TooLong,

    /// <summary>A text that does not match its pattern.</summary>
    Pattern,

    /// <summary>A number below its least, the <see cref="ValueProblem.Limit" />.</summary>
    BelowMin,

    /// <summary>A number above its greatest, the <see cref="ValueProblem.Limit" />.</summary>
    AboveMax,

    /// <summary>A number with more decimal places than its scale, the <see cref="ValueProblem.Limit" />.</summary>
    TooManyDecimals,

    /// <summary>A number with more digits before the point than its precision leaves room for.</summary>
    TooManyDigits,

    /// <summary>A day before its earliest, the <see cref="ValueProblem.Limit" />.</summary>
    BeforeEarliest,

    /// <summary>A day after its latest, the <see cref="ValueProblem.Limit" />.</summary>
    AfterLatest,

    /// <summary>A range's end before its start, the <see cref="ValueProblem.Limit" />.</summary>
    EndBeforeStart,

    /// <summary>A strict range's end not after its start, the <see cref="ValueProblem.Limit" />.</summary>
    EndNotAfterStart,
}
