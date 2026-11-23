using System.Globalization;

namespace Rask.Data;

/// <summary>
///     The base of a value object that holds one calendar day:
///     <c>public sealed partial record InvoiceDay(DateOnly Value) : Day(Value, NotAfterToday: true);</c>
/// </summary>
/// <remarks>
///     The constructor checks the bounds and throws a <see cref="Rask.Cqrs.RaskValidationException" /> when one refuses
///     the day; the generated half is <see cref="Text" />'s, for a <see cref="DateOnly" />. "Today" is
///     <see cref="Clock.Today" />, the day on the app's wall clock, so a test that sets the clock moves it. Two values
///     are equal when they are of the same type and hold the same day.
/// </remarks>
public abstract record Day : Value
{
    /// <summary>Keeps <paramref name="Value" /> once the bounds the other arguments declare accept it.</summary>
    /// <param name="Value">The day as given; the derived record's own positional parameter.</param>
    /// <param name="NotBefore">The earliest day it may be, or null.</param>
    /// <param name="NotAfter">The latest day it may be, or null.</param>
    /// <param name="NotAfterToday">Whether a day after today is refused.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="NotBefore" /> is after <paramref name="NotAfter" />.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A bound refuses the day.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Day(Value, NotAfterToday: true)`
    protected Day(DateOnly Value, DateOnly? NotBefore = null, DateOnly? NotAfter = null, bool NotAfterToday = false)
#pragma warning restore IDE1006
        : base(checkNow: false)
    {
        if (NotBefore > NotAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(NotBefore), NotBefore, "The earliest day is after the latest.");
        }

        this.Value = Value;
        if (!Loading.IsOn)
        {
            Check(Problems(Value, NotBefore, NotAfter, NotAfterToday));
        }
    }

    /// <summary>The day it holds.</summary>
    public DateOnly Value { get; }

    /// <summary>The day it holds, written in the current culture.</summary>
    /// <returns>The day as text.</returns>
    public sealed override string ToString() => Value.ToString(CultureInfo.CurrentCulture);

    private static List<Problem>? Problems(DateOnly day, DateOnly? notBefore, DateOnly? notAfter, bool notAfterToday)
    {
        if (day < notBefore)
        {
            return [Say(ValueProblemKind.BeforeEarliest, notBefore)];
        }

        var latest = notAfter;
        if (notAfterToday && Clock.Today is var today && (latest is not { } declared || today < declared))
        {
            latest = today;
        }

        return day > latest ? [Say(ValueProblemKind.AfterLatest, latest)] : null;
    }

    private static Problem Say(ValueProblemKind kind, object? limit) =>
        Problem.Whole(ValueWords.Say(new ValueProblem(kind, limit)));
}
