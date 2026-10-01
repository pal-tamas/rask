namespace Rask.Testing;

/// <summary><c>Clock.Fake(at: …)</c> — declared here so it exists only where Rask.Testing is referenced.</summary>
public static class ClockFakes
{
    extension(Clock)
    {
        /// <summary>
        ///     Freezes the app's clock at <paramref name="at" /> for this test until the returned clock is disposed:
        ///     <c>using var clock = Clock.Fake(at: monday9am);</c>. <c>Clock.Now</c>, <c>3.Days.Ago</c>, cache expiry and
        ///     audit stamps all read it; move it on with <c>clock.Advance(2.Hours)</c>.
        /// </summary>
        /// <remarks>Scoped to the test's own flow, so tests running in parallel never see each other's time.</remarks>
        public static ClockFake Fake(DateTimeOffset at) => new(at);
    }
}
