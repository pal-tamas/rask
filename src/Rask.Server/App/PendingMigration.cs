namespace Rask;

/// <summary>A migration the database has not had yet.</summary>
/// <param name="Id">The migration's id, as EF Core names it.</param>
/// <param name="Exclusive">
///     Whether it rewrites what the rows hold, so an instance still on the old code would misread them: the old instance
///     is stopped before it runs.
/// </param>
internal sealed record PendingMigration(string Id, bool Exclusive);
