namespace Rask.Background;

/// <summary>A test's stand-in for the job queue: <c>using var jobs = Jobs.Fake();</c>.</summary>
public static class JobsFakes
{
    extension(Jobs)
    {
        /// <summary>
        ///     Takes the place of the job queue for this test, recording every enqueue instead of writing a
        ///     row, until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var jobs = Jobs.Fake();
        ///
        ///     await page.Click("Place order");
        ///
        ///     jobs.Enqueued&lt;SendOrderReceipt&gt;().Once();
        ///     jobs.Enqueued&lt;ChaseInvoice&gt;().In(24.Hours).Once();
        ///     </code>
        ///     <para>
        ///         Nothing runs until the test says so: <c>await jobs.Run()</c> sends every recorded job
        ///         through its real handler. Without it the test asserts that the work was <em>asked for</em>.
        ///         Scoped to the test's own flow, so tests running in parallel never see each other's jobs.
        ///         It stands in front of <c>Jobs.Enqueue</c>; a class that takes <see cref="IJobs" /> in its
        ///         constructor is handed whatever the container holds, so register the fake there too —
        ///         <c>services.AddSingleton&lt;IJobs&gt;(jobs)</c> — when the code under test injects it.
        ///     </para>
        /// </remarks>
        public static JobsFake Fake() => new();
    }
}
