using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The locks that keep one numbering of a group at a time: held by the save's own transaction, so they are let go
///     when it commits or rolls back, and taken before its rows go out, so no save waits on another's row while holding
///     what that one waits for.
/// </summary>
/// <remarks>
///     SQL Server takes <c>sp_getapplock</c> owned by the transaction, PostgreSQL <c>pg_advisory_xact_lock</c> on the
///     name's <c>hashtext</c> — each in a sorted order, so two saves that touch the same groups take them alike. SQLite
///     has one writer at a time, which already keeps them apart.
/// </remarks>
internal static class NumberingLocks
{
    /// <summary>The name of the lock on one group of one numbering.</summary>
    internal static string Name(string numbering, string group)
    {
        var name = "rask:numbered:" + numbering + ":" + group;
        return name.Length <= SqlServerAppLock.MaxNameLength
            ? name
            : "rask:numbered:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
    }

    /// <summary>Takes <paramref name="names" /> in <paramref name="context" />'s transaction in progress.</summary>
    internal static async Task Take(DbContext context, IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0 || context.Database.CurrentTransaction is null)
        {
            return;
        }

        switch (context.Database.ProviderName)
        {
            case ProviderNames.SqlServer:
                foreach (var name in names.Order(StringComparer.Ordinal))
                {
                    await context.Database.ExecuteSqlRawAsync(SqlServerAppLock.TakeForTransaction, [name], cancellationToken)
                        .ConfigureAwait(false);
                }

                break;
            case ProviderNames.Postgres:
                var keys = new SortedSet<int>();
                foreach (var name in names)
                {
                    keys.Add(await context.Database.SqlQueryRaw<int>("SELECT hashtext({0}) AS \"Value\"", name)
                        .SingleAsync(cancellationToken).ConfigureAwait(false));
                }

                foreach (var key in keys)
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0})", [key], cancellationToken).ConfigureAwait(false);
                }

                break;
        }
    }
}
