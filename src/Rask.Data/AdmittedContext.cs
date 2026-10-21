using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     A caller's <c>db:</c> context as it was before <see cref="AmbientTransaction" /> enlisted it: its connection,
///     whether it owned that connection, and its connection string — so the caller gets it back exactly so.
/// </summary>
internal sealed class AdmittedContext
{
    private readonly DbContext _context;
    private readonly DbConnection _connection;
    private readonly bool _owned;
    private readonly string? _connectionString;

    private AdmittedContext(DbContext context, DbConnection connection, bool owned, string? connectionString)
    {
        _context = context;
        _connection = connection;
        _owned = owned;
        _connectionString = connectionString;
    }

    /// <summary>What <paramref name="context" /> is on now, taken before anything points it elsewhere.</summary>
    internal static AdmittedContext Capture(DbContext context)
    {
        var connection = context.Database.GetDbConnection();

        // EF owns the connection it created from a connection string; one given in the options is owned as they said.
        var configured = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        var owned = configured.Connection is null ||
                    (ReferenceEquals(configured.Connection, connection) && configured.IsConnectionOwned);
        return new AdmittedContext(context, connection, owned, context.Database.GetConnectionString());
    }

    /// <summary>Takes the context out of <paramref name="transaction" /> and puts it back on its own connection.</summary>
    internal void Restore(DbTransaction transaction)
    {
        var joined = _context.Database.CurrentTransaction;
        if (ReferenceEquals(joined?.GetDbTransaction(), transaction))
        {
            // Not owned by the context: disposing it leaves the transaction to its owner and only undoes the joining.
            joined.Dispose();
        }

        if (ReferenceEquals(_context.Database.GetDbConnection(), _connection))
        {
            return;
        }

        if (_owned)
        {
            // The one it owned went with the joining, so it opens a fresh one from its connection string, as before.
            _context.Database.SetDbConnection(null, contextOwnsConnection: true);
            _context.Database.SetConnectionString(_connectionString);
        }
        else
        {
            _context.Database.SetDbConnection(_connection, contextOwnsConnection: false);
        }
    }
}
