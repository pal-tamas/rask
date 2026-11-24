using System.Globalization;

namespace Rask.Values;

/// <summary>
///     The base of a value object that holds one moment, as the clock on the wall shows it:
///     <c>public sealed partial record FuelingMoment(DateTime Value) : Moment(Value, NotAfterNow: true);</c>
/// </summary>
/// <remarks>
///     <para>
///         The moment is a wall-clock time in <see cref="Clock.Zone" /> — what <see cref="Clock.Wall" /> reads — and
///         its <see cref="DateTime.Kind" /> is always <see cref="DateTimeKind.Unspecified" />, which is how a
///         <c>datetime2</c> or SQLite <c>TEXT</c> column keeps it and reads it back. A UTC time is never kept as one:
///         a <see cref="DateTime" /> of <see cref="DateTimeKind.Utc" /> is converted to <see cref="Clock.Zone" />, and
///         one of <see cref="DateTimeKind.Local" /> from the machine's zone to it, so <c>Create(Clock.Now.UtcDateTime)</c>
///         and <c>Create(Clock.Wall)</c> hold the same moment.
///     </para>
///     <para>
///         The constructor checks the bounds and throws a <see cref="Rask.Cqrs.RaskValidationException" /> when one
///         refuses the moment; "now" is <see cref="Clock.Wall" />, so a test that sets the clock moves it. Two values
///         are equal when they are of the same type and hold the same moment.
///     </para>
/// </remarks>
public abstract record Moment : Value
{
    /// <summary>Keeps <paramref name="Value" />, on the wall clock, once the bounds the other arguments declare accept it.</summary>
    /// <param name="Value">The moment as given; the derived record's own positional parameter.</param>
    /// <param name="NotBefore">The earliest moment it may be, on the wall clock, or null.</param>
    /// <param name="NotAfter">The latest moment it may be, on the wall clock, or null.</param>
    /// <param name="NotAfterNow">Whether a moment after now is refused.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="NotBefore" /> is after <paramref name="NotAfter" />.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A bound refuses the moment.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Moment(Value, NotAfterNow: true)`
    protected Moment(DateTime Value, DateTime? NotBefore = null, DateTime? NotAfter = null, bool NotAfterNow = false)
#pragma warning restore IDE1006
        : base(checkNow: false)
    {
        if (NotBefore > NotAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(NotBefore), NotBefore, "The earliest moment is after the latest.");
        }

        if (DeclaredRules.IsReading)
        {
            DeclaredRules.Note(new MomentRules(NotBefore, NotAfter, NotAfterNow));
        }

        this.Value = OnTheWall(Value);
        if (!RowLoading.IsOn)
        {
            Check(Problems(this.Value, NotBefore, NotAfter, NotAfterNow));
        }
    }

    /// <summary>The moment it holds, on the wall clock of <see cref="Clock.Zone" />.</summary>
    public DateTime Value { get; }

    /// <summary>The day of the moment, as the calendar on the wall shows it.</summary>
    public DateOnly Day => DateOnly.FromDateTime(Value);

    /// <summary>The moment it holds, written culture-invariant: a value's text, not its display (that is the UI's).</summary>
    /// <returns>The moment as text.</returns>
    public sealed override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    // A wall-clock time stays as given; a UTC or machine-local one is moved onto the app's wall clock. The conversion
    // hands back Utc or Local where the zone is one of those, so the kind is set after it.
    private static DateTime OnTheWall(DateTime given) =>
        given.Kind is DateTimeKind.Unspecified
            ? given
            : DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(given, Clock.Zone), DateTimeKind.Unspecified);

    private static List<Problem>? Problems(DateTime moment, DateTime? notBefore, DateTime? notAfter, bool notAfterNow)
    {
        if (moment < notBefore)
        {
            return [Say(ValueProblemKind.BeforeEarliest, notBefore)];
        }

        var latest = notAfter;
        if (notAfterNow && Clock.Wall is var now && (latest is not { } declared || now < declared))
        {
            latest = now;
        }

        return moment > latest ? [Say(ValueProblemKind.AfterLatest, latest)] : null;
    }

    private static Problem Say(ValueProblemKind kind, object? limit) =>
        Problem.Whole(ValueWords.Say(new ValueProblem(kind, limit)));
}
