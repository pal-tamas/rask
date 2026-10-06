namespace Rask.Cqrs;

/// <summary>A test's stand-in for the outbox: <c>using var outbox = Outbox.Fake();</c>.</summary>
public static class OutboxFakes
{
    extension(Outbox)
    {
        /// <summary>
        ///     Takes the place of the outbox table for this test, recording every event stored for a durable
        ///     handler instead of writing a row, until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var outbox = Outbox.Fake();
        ///
        ///     await page.Click("Place order");
        ///     await outbox.Run();
        ///
        ///     outbox.Stored&lt;OrderPlaced&gt;().Once();
        ///     </code>
        ///     <para>
        ///         An <c>IEventHandler</c> still runs at once, as it always does. A durable handler runs when
        ///         the test says so: <c>await outbox.Run()</c>. Scoped to the test's own flow, so tests running
        ///         in parallel never see each other's events.
        ///     </para>
        /// </remarks>
        public static OutboxFake Fake() => new();
    }
}
