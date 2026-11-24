using Rask.Core.Globalization;

namespace Rask.Values;

/// <summary>
///     How the rules of <see cref="Text" />, <see cref="Number" />, <see cref="Whole" />, <see cref="Day" /> and
///     <see cref="Range" /> are worded: from Rask's strings catalog, so they are said in the reader's language, and in
///     English where the catalog says nothing.
/// </summary>
internal static class ValueWords
{
    /// <summary>What <paramref name="problem" /> says: its own message, else the catalog's words, else the English.</summary>
    public static string Say(ValueProblem problem) =>
        problem.Message ?? problem.Kind switch
        {
            ValueProblemKind.Required => RaskStrings.Get(RaskString.ValidationRequired, problem.Template),
            ValueProblemKind.TooShort => RaskStrings.Get(RaskString.ValidationTooShort, problem.Template, problem.Limit),
            ValueProblemKind.TooLong => RaskStrings.Get(RaskString.ValidationTooLong, problem.Template, problem.Limit),
            ValueProblemKind.Email => RaskStrings.Get(RaskString.ValidationEmail, problem.Template),
            ValueProblemKind.Phone => RaskStrings.Get(RaskString.ValidationPhone, problem.Template),
            ValueProblemKind.Url => RaskStrings.Get(RaskString.ValidationWebAddress, problem.Template),
            ValueProblemKind.Pattern => RaskStrings.Get(RaskString.ValidationPattern, problem.Template),
            ValueProblemKind.BelowMin => RaskStrings.Get(RaskString.ValidationNumberMin, problem.Template, problem.Limit),
            ValueProblemKind.AboveMax => RaskStrings.Get(RaskString.ValidationNumberMax, problem.Template, problem.Limit),
            ValueProblemKind.TooManyDecimals => RaskStrings.Get(RaskString.ValidationDecimals, problem.Template, problem.Limit),
            ValueProblemKind.TooManyDigits => RaskStrings.Get(RaskString.ValidationNumberRange, problem.Template),
            ValueProblemKind.BeforeEarliest => RaskStrings.Get(RaskString.ValidationDateMin, problem.Template, problem.Limit),
            ValueProblemKind.AfterLatest => RaskStrings.Get(RaskString.ValidationDateMax, problem.Template, problem.Limit),
            ValueProblemKind.EndBeforeStart => RaskStrings.Get(RaskString.ValidationRangeNotBefore, problem.Template, problem.Limit),
            ValueProblemKind.EndNotAfterStart => RaskStrings.Get(RaskString.ValidationRangeAfter, problem.Template, problem.Limit),
            _ => problem.English,
        };
}
