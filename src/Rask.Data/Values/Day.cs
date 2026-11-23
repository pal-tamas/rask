using System.Globalization;

namespace Rask.Data;

/// <summary>
///     The base of a value object that holds one calendar day: <c>public sealed partial record InvoiceDay() : Day(NotAfterToday: true);</c>
/// </summary>
/// <remarks>
///     The generated half is <see cref="Text" />'s, for a <see cref="DateOnly" />: <c>InvoiceDay.Create(value)</c>
///     checks the rules. "Today" is <see cref="Clock.Today" />, the day on the app's wall clock, so a test that sets
///     the clock moves it. Two values are equal when they are of the same type and hold the same day.
/// </remarks>
public abstract record Day
{
    private readonly DateOnly? _notBefore;
    private readonly DateOnly? _notAfter;
    private readonly bool _notAfterToday;

    /// <summary>Declares the rules: <c>Day(NotAfterToday: true)</c>.</summary>
    /// <param name="NotBefore">The earliest day it may be, or null.</param>
    /// <param name="NotAfter">The latest day it may be, or null.</param>
    /// <param name="NotAfterToday">Whether a day after today is refused.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="NotBefore" /> is after <paramref name="NotAfter" />.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Day(NotAfterToday: true)`
    protected Day(DateOnly? NotBefore = null, DateOnly? NotAfter = null, bool NotAfterToday = false)
#pragma warning restore IDE1006
    {
        if (NotBefore > NotAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(NotBefore), NotBefore, "The earliest day is after the latest.");
        }

        _notBefore = NotBefore;
        _notAfter = NotAfter;
        _notAfterToday = NotAfterToday;
    }

    /// <summary>The day it holds.</summary>
    public DateOnly Value { get; protected init; }

    /// <summary>The earliest day it may be, or null.</summary>
    public DateOnly? NotBefore => _notBefore;

    /// <summary>The latest day it may be, or null.</summary>
    public DateOnly? NotAfter => _notAfter;

    /// <summary>Whether a day after today is refused.</summary>
    public bool NotAfterToday => _notAfterToday;

    /// <summary>The same type, holding the same day.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>Whether the two are the same value.</returns>
    public virtual bool Equals(Day? other) =>
        ReferenceEquals(this, other) || (other is not null && EqualityContract == other.EqualityContract && Value == other.Value);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EqualityContract, Value);

    /// <summary>The day it holds, written in the current culture.</summary>
    /// <returns>The day as text.</returns>
    public sealed override string ToString() => Value.ToString(CultureInfo.CurrentCulture);

    /// <summary>What a given day is kept as: itself, unless a value object says otherwise.</summary>
    /// <param name="value">The day as given.</param>
    /// <returns>The day to check and keep.</returns>
    protected virtual DateOnly Normalize(DateOnly value) => value;

    /// <summary>The rules <paramref name="value" /> breaks, after <see cref="Normalize" />. Asked by generated code.</summary>
    /// <param name="value">The day as given, or null for none.</param>
    /// <returns>Each rule it breaks; none when it may be kept.</returns>
    protected IEnumerable<ValueProblem> Problems(DateOnly? value)
    {
        if (value is not { } given)
        {
            return [new ValueProblem(ValueProblemKind.Required)];
        }

        var day = Normalize(given);

        if (day < _notBefore)
        {
            return [new ValueProblem(ValueProblemKind.BeforeEarliest, _notBefore)];
        }

        var latest = _notAfter;
        if (_notAfterToday && Clock.Today is var today && (latest is not { } declared || today < declared))
        {
            latest = today;
        }

        return day > latest ? [new ValueProblem(ValueProblemKind.AfterLatest, latest)] : [];
    }
}
