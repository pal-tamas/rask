using System.Globalization;

namespace Rask.Values;

/// <summary>
///     The base of a value object that holds one moment, as the clock on the wall shows it:
///     <c>public sealed partial record FuelingMoment(DateTime Value) : Moment(Value, NotAfterNow: true);</c>
/// </summary>
/// <remarks>
///     <para>
///         A moment is an instant — <see cref="Instant" />, which is what every database keeps, as UTC — shown on the wall
///         clock of <see cref="Clock.Zone" />: <see cref="Value" /> is what <see cref="Clock.Wall" /> read at it, of
///         <see cref="DateTimeKind.Unspecified" />. A <see cref="DateTime" /> of <see cref="DateTimeKind.Utc" /> is the
///         instant it names, one of <see cref="DateTimeKind.Local" /> is converted from the machine's zone, and an
///         unspecified one is a wall-clock time of <see cref="Clock.Zone" />, so <c>Create(Clock.Now.UtcDateTime)</c> and
///         <c>Create(Clock.Wall)</c> hold the same moment. A wall-clock time of another zone — a device reporting its own
///         local time — becomes one through the generated <c>FromWall(local, zone)</c>.
///     </para>
///     <para>
///         A wall time names its instant by one rule, the one SQL Server's <c>AT TIME ZONE</c> follows: one the clock shows
///         twice when summer time ends is its FIRST occurrence (still in summer time), and one the clock skips when it
///         begins is moved forward by the jump — <c>02:30</c> is held as <c>03:30</c>. A moment made from its instant
///         keeps it exactly, either side of the change.
///     </para>
///     <para>
///         The constructor checks the bounds and throws a <see cref="Rask.Cqrs.RaskValidationException" /> when one
///         refuses the moment; "now" is <see cref="Clock.Wall" />, so a test that sets the clock moves it. Two values
///         are equal when they are of the same type and hold the same instant.
///     </para>
/// </remarks>
public abstract record Moment : Value
{
    /// <summary>Keeps <paramref name="Value" />, on the wall clock, once the bounds the other arguments declare accept it.</summary>
    /// <param name="Value">The moment as given; the derived record's own positional parameter.</param>
    /// <param name="NotBefore">The earliest moment it may be, on the wall clock, or null.</param>
    /// <param name="NotAfter">The latest moment it may be, on the wall clock, or null.</param>
    /// <param name="NotAfterNow">Whether a moment after now is refused.</param>
    /// <param name="Message">
    ///     What a moment that breaks a rule declared here is told, said once however many it breaks; or null for the
    ///     framework's words for each.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="NotBefore" /> is after <paramref name="NotAfter" />.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A bound refuses the moment.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Moment(Value, NotAfterNow: true)`
    protected Moment(DateTime Value, DateTime? NotBefore = null, DateTime? NotAfter = null, bool NotAfterNow = false, string? Message = null)
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

        var zone = Clock.Zone;
        var utc = WallClock.ToUtc(Value, zone);
        Instant = new DateTimeOffset(utc);
        this.Value = WallClock.ToWall(utc, zone);
        if (!RowLoading.IsOn)
        {
            Check(Problems(this.Value, NotBefore, NotAfter, NotAfterNow, Message));
        }
    }

    /// <summary>The moment it holds, on the wall clock of <see cref="Clock.Zone" />, of <see cref="DateTimeKind.Unspecified" />.</summary>
    public DateTime Value { get; }

    /// <summary>The instant it holds, in UTC: what a database keeps, and what two moments are compared by.</summary>
    public DateTimeOffset Instant { get; }

    /// <summary>The day of the moment, as the calendar on the wall shows it.</summary>
    public DateOnly Day => DateOnly.FromDateTime(Value);

    /// <summary>
    ///     The moment on the wall clock of <paramref name="zone" />: what a device that keeps that zone's local time is
    ///     told, <c>moment.WallIn(pumpZone)</c>.
    /// </summary>
    /// <param name="zone">The zone whose clocks show it.</param>
    /// <returns>The wall-clock time, of <see cref="DateTimeKind.Unspecified" />.</returns>
    public DateTime WallIn(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return WallClock.ToWall(Instant.UtcDateTime, zone);
    }

    /// <summary>The moment it holds, written as ISO 8601, culture-invariant: a value's text, not its display (that is the UI's).</summary>
    /// <returns>The moment as text.</returns>
    public sealed override string ToString() => Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static List<Problem>? Problems(DateTime moment, DateTime? notBefore, DateTime? notAfter, bool notAfterNow, string? message)
    {
        if (moment < notBefore)
        {
            return [Say(ValueProblemKind.BeforeEarliest, notBefore, message)];
        }

        var latest = notAfter;
        if (notAfterNow && Clock.Wall is var now && (latest is not { } declared || now < declared))
        {
            latest = now;
        }

        return moment > latest ? [Say(ValueProblemKind.AfterLatest, latest, message)] : null;
    }

    private static Problem Say(ValueProblemKind kind, object? limit, string? message) =>
        Problem.Whole(ValueWords.Say(new ValueProblem(kind, limit, message)));
}
