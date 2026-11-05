namespace Rask.Data;

/// <summary>
///     How <see cref="TestDatabase.FromEnvironment{TContext}" /> uses the database its variable names.
/// </summary>
public sealed class TestDatabaseOptions
{
    /// <summary>
    ///     Run against the named database itself — a standing copy kept for tests — instead of a fresh one per test.
    ///     <see langword="false" /> by default.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         By default every test gets a database of its own, named after the one the variable names with a unique
    ///         suffix: created and migrated before the test, dropped after it. Nothing a test writes reaches another.
    ///     </para>
    ///     <para>
    ///         Shared, the database is migrated if it is behind and never created or dropped, and the test holds
    ///         <see cref="LockFile" /> while it runs — so two test runs on one machine, in one process or in two, take
    ///         turns at it rather than writing over each other. What one test leaves behind, the next one sees.
    ///     </para>
    /// </remarks>
    public bool Shared { get; set; }

    /// <summary>
    ///     The file a shared database is locked by, held exclusively for the length of each test. By default one per
    ///     connection string, in the temporary directory.
    /// </summary>
    /// <remarks>
    ///     An exclusive advisory lock on the file — the one <c>lockf</c> and <c>flock</c> take — so a script that runs
    ///     its own tests under <c>lockf /tmp/app-test-db.lock …</c> waits for these, and these wait for it, once both
    ///     name the same file.
    /// </remarks>
    public string? LockFile { get; set; }
}
