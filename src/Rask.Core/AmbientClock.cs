namespace Rask.Core;

/// <summary>
///     The clock <c>3.Days.Ago</c> and <c>2.Hours.FromNow</c> read: a test's frozen clock when one is in
///     scope, else the app's <see cref="TimeProvider" />, else the system clock.
/// </summary>
/// <remarks>
///     The override is per async flow, so parallel tests that each freeze time never see each other's.
/// </remarks>
internal static class AmbientClock
{
    private static readonly AsyncLocal<TimeProvider?> Override = new();

    /// <summary>The app-wide clock, which a host sets from its registered <see cref="TimeProvider" />.</summary>
    internal static TimeProvider Default { get; set; } = TimeProvider.System;

    internal static TimeProvider Current => Override.Value ?? Default;

    internal static DateTimeOffset Now => Current.GetUtcNow();

    /// <summary>Uses <paramref name="clock" /> in this async flow until the returned scope is disposed.</summary>
    internal static IDisposable Use(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var previous = Override.Value;
        Override.Value = clock;
        return new Scope(previous);
    }

    private sealed class Scope(TimeProvider? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
