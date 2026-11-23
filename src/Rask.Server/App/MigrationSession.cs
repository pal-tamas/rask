using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Server;

/// <summary>
///     The connection a start's migrations run on, with the session limits an app's queries are held to lifted: Rask.Postgres'
///     <c>statement_timeout</c> and <c>lock_timeout</c> and Rask.SqlServer's <c>LOCK_TIMEOUT</c> are made for a request, and a
///     migration that rewrites a table or waits behind an old instance's queries would be killed by them partway.
/// </summary>
/// <remarks>
///     Opened once and held for the whole migration, so EF runs every migration command on it rather than on a fresh
///     connection that would take the limits again. The pool puts the session back as it came when it is returned: Npgsql's
///     <c>DISCARD ALL</c> and SqlClient's reset, followed on the next open by Rask.SqlServer's own script.
/// </remarks>
internal static class MigrationSession
{
    /// <summary>What lifts the session limits on <paramref name="providerName" />'s connection, or null when it keeps none.</summary>
    internal static string? LiftLimits(string? providerName) => providerName switch
    {
        ProviderNames.Postgres => "SET statement_timeout = 0; SET lock_timeout = 0;",
        ProviderNames.SqlServer => "SET LOCK_TIMEOUT -1;",
        _ => null,
    };

    /// <summary>
    ///     Opens <paramref name="db" />'s connection for its migrations, the limits lifted, creating the database first when the
    ///     server does not have it yet; closing it hands it back.
    /// </summary>
    internal static async Task<IAsyncDisposable> Open(DbContext db, CancellationToken cancellationToken)
    {
        await MissingDatabase.Create(db, cancellationToken).ConfigureAwait(false);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (LiftLimits(db.Database.ProviderName) is { } lift)
        {
            await db.Database.ExecuteSqlRawAsync(lift, cancellationToken).ConfigureAwait(false);
        }

        return new Closing(db);
    }

    private sealed class Closing(DbContext db) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await db.Database.CloseConnectionAsync().ConfigureAwait(false);
    }
}
