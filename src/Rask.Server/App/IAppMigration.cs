namespace Rask;

/// <summary>
///     The app's migrations applied the way its start applies them (<see cref="MigrateOnStart{TContext}" />): the pending
///     schema migrations, then the data steps not yet run — what <c>dotnet App.dll rask db migrate</c> runs.
/// </summary>
internal interface IAppMigration
{
    /// <summary>Brings the database up to date: its schema, then its data steps.</summary>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>The task.</returns>
    Task MigrateAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The migrations the database has not had yet, in the order they apply — what <c>rask db pending</c> lists, and
    ///     <c>rask deploy</c> asks a new image before it starts it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the look.</param>
    /// <returns>Each pending migration, and whether no older version of the app may run beside it.</returns>
    Task<IReadOnlyList<PendingMigration>> PendingAsync(CancellationToken cancellationToken);
}
