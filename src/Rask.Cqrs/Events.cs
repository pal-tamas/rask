namespace Rask.Cqrs;

/// <summary>A test's stand-in for event publishing: <c>using var events = Events.Fake();</c>.</summary>
public static class Events
{
    // How many fakes are open anywhere, so a publish outside any test reads one integer and not the AsyncLocal.
    private static int _open;

    /// <summary>What <c>Events.Fake()</c> put in the way of publishing, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<EventsFake?> Faked = new();

    /// <summary>
    ///     Takes the place of publishing for this test, recording every event instead of running its handlers, until the
    ///     returned fake is disposed:
    /// </summary>
    /// <remarks>
    ///     <code>
    ///     using var events = Events.Fake();
    ///
    ///     await Dispatcher.Send(new ChangeDriverChips(driver.Id, chips));
    ///
    ///     events.Published&lt;DriverChipsChanged&gt;().Once();
    ///     events.Published&lt;DriverChipsChanged&gt;().Where(e =&gt; e.DriverId == driver.Id).Once();
    ///     </code>
    ///     <para>
    ///         It records what <c>Dispatcher.Publish</c> (or an injected <see cref="IDispatcher" />) is handed, and so the
    ///         domain events an aggregate raises, which its save publishes once the change commits (with
    ///         <c>AddRaskCqrs()</c> registered, as a <c>RaskApp</c> has it).
    ///     </para>
    ///     <para>
    ///         Nothing runs until the test says so, as with <c>Jobs.Fake()</c>: no handler, no subscription, no open page
    ///         hears a recorded event. <c>await events.Run()</c> publishes every recorded event for real; without it the
    ///         test asserts that the event was <em>published</em>. A durable handler the outbox already stored in the save's
    ///         transaction is the outbox's to run (<c>Outbox.Fake()</c>). Scoped to the test's own flow, so tests running
    ///         in parallel never see each other's events.
    ///     </para>
    /// </remarks>
    public static EventsFake Fake() => new();

    internal static void Opened() => Interlocked.Increment(ref _open);

    internal static void Closed() => Interlocked.Decrement(ref _open);

    /// <summary>
    ///     Whether a fake of this flow took <paramref name="e" />, so it must not be published: recorded, with the services
    ///     its later <c>Run()</c> publishes it from.
    /// </summary>
    internal static bool Recorded(IEvent e, IServiceProvider? services) =>
        Volatile.Read(ref _open) > 0 && Faked.Value is { } fake && fake.Record(e, services);
}
