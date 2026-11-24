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
}
