namespace Rask.Logging;

/// <summary>A test's stand-in for the durable log: <c>using var logs = Logs.Fake();</c>.</summary>
public static class LogsFakes
{
    extension(Logs)
    {
        /// <summary>
        ///     Takes the place of the log store for this test — an in-memory one that really stores, so a
        ///     search reads back what was written — until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var logs = Logs.Fake();
        ///
        ///     await page.Click("Save");
        ///
        ///     logs.Stored().AtLeast(LogLevel.Error).Saying("refused").Once();
        ///     </code>
        ///     <para>
        ///         Scoped to the test's own flow, so tests running in parallel never see each other's
        ///         entries. It stands in front of <c>Logs.Search</c>; a class that takes <see cref="ILogs" />
        ///         in its constructor is handed whatever the container holds, so register the fake there too
        ///         — <c>services.AddSingleton&lt;ILogs&gt;(logs)</c> — when the code under test injects it.
        ///     </para>
        ///     <para>
        ///         It records what reached the STORE, not what was logged: the real pillar drains an
        ///         <c>ILogger</c> through a channel and a background writer, so a test that wants to prove a
        ///         line was logged should append to this directly rather than expect a logger call to arrive.
        ///     </para>
        /// </remarks>
        public static LogsFake Fake() => new();
    }
}
