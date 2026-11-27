using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
///     Registers, on every connection a context opens, the collating sequences its model needs: the invariant
///     <c>EF_DECIMAL</c> and <see cref="SqliteCollations.SearchOrder" /> always, and <see cref="SqliteCollations.TextOrder" /> — with <c>instr</c> and <c>LIKE</c> compared
///     the same way — when the model names how text compares with <c>OrderTextBy</c>.
/// </summary>
/// <remarks>
///     A service of <see cref="TextCollationOptionsExtension" />, so <c>UseRaskSqlite</c> on a server and
///     <c>UseRaskFullTextSearch</c> on a browser's plain <c>UseSqlite</c> register it once between them. A pooled
///     connection's <c>Deactivate()</c> un-registers what is registered, so it runs on every open; a context pointed at a
///     <c>Db.Transaction</c>'s connection, which has them already, is left alone.
/// </remarks>
internal sealed class SqliteCollationInterceptor : DbConnectionInterceptor
{
    internal static readonly SqliteCollationInterceptor Instance = new();

    private SqliteCollationInterceptor()
    {
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection, eventData);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection, eventData);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is not SqliteConnection sqlite || eventData.Context?.Database.CurrentTransaction is not null)
        {
            return;
        }

        SqliteCollations.Apply(sqlite);
        SqliteSearchText.Apply(sqlite);
        SqliteMoments.Apply(sqlite);
        if (eventData.Context?.Model.FindAnnotation(TextCollation.Annotation)?.Value is string name)
        {
            var collation = TextCollation.Parse(name);
            SqliteTextOrder.Apply(sqlite, collation.Culture.CompareInfo, collation.Options);
        }
    }
}
