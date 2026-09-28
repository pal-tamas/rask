namespace Rask.Storage;

/// <summary>A test's stand-in for file storage: <c>using var files = Files.Fake();</c>.</summary>
public static class FilesFakes
{
    extension(Files)
    {
        /// <summary>
        ///     Takes the place of file storage for this test — an in-memory one that really stores, so the
        ///     code under test reads back what it saved — until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var files = Files.Fake();
        ///
        ///     await page.Click("Upload");
        ///
        ///     files.Saved().Named("avatar.png").Public().Once();
        ///     </code>
        ///     <para>
        ///         Nothing touches a disk, a bucket or a database. Scoped to the test's own flow, so tests
        ///         running in parallel never see each other's files. It stands in front of <c>Files.Save</c>;
        ///         a class that takes <see cref="IFiles" /> in its constructor is handed whatever the
        ///         container holds, so register the fake there too —
        ///         <c>services.AddSingleton&lt;IFiles&gt;(files)</c> — when the code under test injects it.
        ///     </para>
        /// </remarks>
        public static FilesFake Fake() => new();
    }
}
