using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     The first column of a query Rask writes itself, read as the text it holds — through the context's connection,
///     with no model in between, so a value the model could not read comes back as stored.
/// </summary>
internal static class StoredText
{
    /// <summary>Each row's first column, as text, streamed.</summary>
    /// <param name="context">The context whose connection (and transaction, when one is open) the query runs on.</param>
    /// <param name="sql">The query: names quoted by the provider, no value of anyone's in it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The texts, in the order the query returns them.</returns>
    internal static async IAsyncEnumerable<string> Read(
        DbContext context, string sql, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            await using (command.ConfigureAwait(false))
            {
#pragma warning disable CA2100 // the names are the model's own, quoted by the provider; no value of anyone's is in it
                command.CommandText = sql;
#pragma warning restore CA2100
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        yield return Text(reader);
                    }
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    // A text column comes back as a string from every provider; anything else is spelled the invariant way.
    private static string Text(DbDataReader reader) =>
        reader.GetValue(0) is string text ? text : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "";
}
