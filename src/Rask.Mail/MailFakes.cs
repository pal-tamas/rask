namespace Rask.Mailing;

/// <summary>A test's stand-in for the mail battery: <c>using var mail = Mail.Fake();</c>.</summary>
public static class MailFakes
{
    extension(Mail)
    {
        /// <summary>
        ///     Takes the place of the mail battery for this test, recording every send instead of queueing
        ///     it, until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var mail = Mail.Fake();
        ///
        ///     await page.Click("Create account");
        ///
        ///     mail.Sent().To("ann@x.io").Once();
        ///     </code>
        ///     <para>
        ///         Scoped to the test's own flow, so tests running in parallel never see each other's mail.
        ///         It stands in front of <c>Mail.Send</c>; a class that takes <see cref="IMail" /> in its
        ///         constructor is handed whatever the container holds, so register the fake there too —
        ///         <c>services.AddSingleton&lt;IMail&gt;(mail)</c> — when the code under test injects it.
        ///     </para>
        /// </remarks>
        public static MailFake Fake() => new();
    }
}
