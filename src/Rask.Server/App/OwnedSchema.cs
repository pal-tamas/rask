using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Rask.Server;

/// <summary>
///     What <c>OwnsSchema</c> said about <typeparamref name="TContext" /> — a hand-wired host's
///     <c>AddRaskData&lt;TContext&gt;(…).OwnsSchema(…)</c> or a <c>RaskApp</c>'s <c>c.Data.OwnsSchema(…)</c> — applied
///     by its <c>MigrateOnStart</c>.
/// </summary>
/// <typeparam name="TContext">The context whose migrations own the schema.</typeparam>
/// <param name="ownership">What the app said.</param>
/// <param name="turnOff">How this app stops applying migrations at start, ending a sentence: "… turn this off {turnOff}".</param>
internal sealed class OwnedSchema<TContext>(SchemaOwnership ownership, string turnOff)
    where TContext : DbContext
{
    /// <summary>How this app stops applying migrations at start, for the sentence a failed migration ends with.</summary>
    public string TurnOff => turnOff;

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
