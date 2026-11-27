namespace Rask.Values;

/// <summary>
///     A moment Rask records itself — when a row was added, changed or deleted (<c>CreatedAt</c>, <c>UpdatedAt</c>,
///     <c>DeletedAt</c>), when a job runs, a session ends, a mail was sent: the framework's own <see cref="Moment" />,
///     stored as the UTC instant it holds on every database, as an app's moments are.
/// </summary>
/// <remarks>
///     An app's own moments are its own types — <c>FuelingMoment</c>, with the bounds its domain says. A timestamp has
///     none: Rask writes it from <see cref="Clock.Now" />, <c>Timestamp.Now</c>. Read its <see cref="Moment.Instant" />
///     for the instant and its <see cref="Moment.Value" /> for the app's wall clock.
/// </remarks>
/// <param name="Value">The moment: a UTC <see cref="DateTime" /> is the instant, an unspecified one the app's wall clock.</param>
public sealed partial record Timestamp(DateTime Value) : Moment(Value)
{
    /// <summary>Now, as <see cref="Clock.Now" /> reads it.</summary>
    public static Timestamp Now => new(Clock.Now.UtcDateTime);

    /// <summary>The timestamp of <paramref name="instant" />.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The timestamp.</returns>
    public static Timestamp At(DateTimeOffset instant) => new(instant.UtcDateTime);
}
