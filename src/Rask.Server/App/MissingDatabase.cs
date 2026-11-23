using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Server;

/// <summary>
///     Creates a context's database when the server does not have it yet, so the connection a start's migrations run on
///     (<see cref="MigrationSession" />) can be opened on a fresh server.
/// </summary>
/// <remarks>
///     <para>
///         EF Core's <c>Migrate</c> creates a missing database too, but only on a connection it opens itself — a connection
///         opened first, against a database that is not there, fails on SQL Server (error 4060) and on PostgreSQL (3D000).
///         SQLite makes its file on open, and its creator says so.
///     </para>
///     <para>
///         Two hosts starting together on an empty server may both find it missing: the one that loses the race to create
///         it fails, finds it there, and goes on. The migrations themselves stay under EF Core's migration lock.
///     </para>
/// </remarks>
internal static class MissingDatabase
{
    /// <summary>How long a host that lost the race waits for the winner's database: 60 looks, half a second apart.</summary>
    private const int OnlineAttempts = 60;

    private static readonly TimeSpan OnlineInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Creates <paramref name="db" />'s database unless it already exists.</summary>
    internal static async Task Create(DbContext db, CancellationToken cancellationToken)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (await creator.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await creator.CreateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException)
        {
            // Another host created it between the check and the create, which is all this step was for. Anything else
            // that stopped the create is still the start's failure.
            if (!await ComesOnline(creator, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }
        }
    }

    /// <summary>
    ///     Whether the database another host is creating comes online: SQL Server refuses a login to a database it has only
    ///     just created (4060) until its recovery is done, and its creator then answers that it does not exist.
    /// </summary>
    private static async Task<bool> ComesOnline(IRelationalDatabaseCreator creator, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < OnlineAttempts; attempt++)
        {
            if (await creator.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(OnlineInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }
}
