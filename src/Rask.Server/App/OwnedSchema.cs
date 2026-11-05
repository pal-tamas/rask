using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Rask.Server;

/// <summary>What <c>OwnsSchema</c> said about <typeparamref name="TContext" />, applied by its <c>MigrateOnStart</c>.</summary>
/// <typeparam name="TContext">The context whose migrations own the schema.</typeparam>
internal sealed class OwnedSchema<TContext>(SchemaOwnership ownership)
    where TContext : DbContext
{
    /// <summary>
    ///     Readies <paramref name="db" /> for its migrations: the patience on every command, and the baseline written
    ///     down when the database already has the schema.
    /// </summary>
    public async Task Prepare(TContext db, ILogger log, CancellationToken cancellation)
    {
        // A migration that moves the data — every row's moment, say — takes minutes, not a command's thirty seconds.
        if (ownership.Timeout is { } patience)
        {
            db.Database.SetCommandTimeout(patience);
        }

        if (await SchemaTakeover.Run(db, ownership, cancellation).ConfigureAwait(false) is { } baseline)
        {
            MigrateOnStartLog.TakenOver(log, typeof(TContext).Name, baseline);
        }
    }
}
