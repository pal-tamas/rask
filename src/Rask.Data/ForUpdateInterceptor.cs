using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Writes a read face's <c>ForUpdate()</c> in the database's own words (<see cref="ForUpdateSql" />) on the read
///     context, which carries none of the write side's interceptors.
/// </summary>
internal sealed class ForUpdateInterceptor : DbCommandInterceptor
{
    internal static readonly ForUpdateInterceptor Instance = new();

    private ForUpdateInterceptor()
    {
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ForUpdateSql.Apply(command, eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        ForUpdateSql.Apply(command, eventData.Context);
        return ValueTask.FromResult(result);
    }
}
