namespace Rask.Server;

/// <summary>
///     How a context's migrations take over a database whose schema predates them, and how long one may run —
///     what <c>OwnsSchema(s =&gt; …)</c> configures.
/// </summary>
/// <remarks>
///     <para>
///         An app moving onto Rask has a live database its migrations did not make. Their first migration — the
///         baseline — makes the schema that database already has, so there it is only written down as applied, and
///         the migrations after it run as usual:
///     </para>
///     <code>
/// .OwnsSchema(s =&gt; s.TakesOverFrom("WallClockMoments").WhenTable("Tenant").Patience(20.Minutes))
///     </code>
///     <para>
///         A database that does not exist yet, or has no such table, is made by the migrations from the first — so
///         a new developer's empty database and production's old one both end up where the migrations say.
///     </para>
/// </remarks>
public sealed class SchemaOwnership
{
    internal SchemaOwnership()
    {
    }

    /// <summary>The migration of the old set the database must show applied before it is taken over, or null.</summary>
    internal string? Predecessor { get; private set; }

    /// <summary>The table whose presence says the database already has the schema, or null.</summary>
    internal string? Table { get; private set; }

    /// <summary>How long one command of a migration may run, or null for the provider's own timeout.</summary>
    internal TimeSpan? Timeout { get; private set; }

    /// <summary>Whether anything was said that can make a database be taken over rather than migrated from the first.</summary>
    internal bool TakesOver => Predecessor is not null || Table is not null;

    /// <summary>
    ///     Takes over a database the app's previous migrations made: one whose history shows
    ///     <paramref name="migration" /> applied gets the baseline written down as applied too.
    /// </summary>
    /// <param name="migration">
    ///     The old set's last migration — its full id (<c>20261016125209_WallClockMoments</c>) or just its name
    ///     (<c>WallClockMoments</c>), which matches whatever moment it was created at.
    /// </param>
    /// <returns>The same ownership, for the next step.</returns>
    /// <remarks>
    ///     With <see cref="WhenTable" /> as well, it is also a guard: a database that has the table but not this
    ///     migration in its history is in no state the baseline describes, and stops the start rather than being
    ///     written down as one.
    /// </remarks>
    public SchemaOwnership TakesOverFrom(string migration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migration);

        Predecessor = migration;
        return this;
    }

    /// <summary>
    ///     Takes over a database that already has <paramref name="table" />: the baseline is written down as applied,
    ///     even with no migration history at all, rather than run against tables that exist.
    /// </summary>
    /// <param name="table">A table the baseline creates — its name, without the schema.</param>
    /// <returns>The same ownership, for the next step.</returns>
    public SchemaOwnership WhenTable(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        Table = table;
        return this;
    }

    /// <summary>
    ///     How long each command of a migration may run before it is given up on, instead of the provider's thirty
    ///     seconds — a migration that moves the data takes minutes.
    /// </summary>
    /// <param name="timeout">The time one command may take — <c>20.Minutes</c>.</param>
    /// <returns>The same ownership, for the next step.</returns>
    public SchemaOwnership Patience(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        Timeout = timeout;
        return this;
    }

    /// <summary>Whether <paramref name="applied" /> is the migration <see cref="TakesOverFrom" /> named.</summary>
    internal bool IsPredecessor(string applied) =>
        Predecessor is { } name
        && (string.Equals(applied, name, StringComparison.Ordinal)
            || applied.EndsWith("_" + name, StringComparison.Ordinal));
}
