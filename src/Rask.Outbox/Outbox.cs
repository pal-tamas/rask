namespace Rask.Cqrs;

/// <summary>
///     The outbox, as a test sees it: <c>using var outbox = Outbox.Fake();</c>. An app never calls the outbox —
///     it gives a handler <see cref="IDurableHandler{TEvent}" /> and publishes or raises the event.
/// </summary>
/// <remarks>
///     In <c>Rask.Cqrs</c>, beside the handlers it runs, because <c>Rask.Outbox</c> is the namespace and cannot be
///     a type too.
/// </remarks>
public static class Outbox
{
    /// <summary>What <c>Outbox.Fake()</c> put in the way of the outbox table, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<OutboxFake?> Faked = new();
}
