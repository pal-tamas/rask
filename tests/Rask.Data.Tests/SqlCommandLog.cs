using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

/// <summary>Every query a context sends, so a test can say a read was one statement.</summary>
public sealed class SqlCommandLog : DbCommandInterceptor
{
    private readonly List<string> _texts = [];

    public IReadOnlyList<string> Texts
    {
        get
        {
            lock (_texts)
            {
                return [.. _texts];
            }
        }
    }

    public void Clear()
    {
        lock (_texts)
        {
            _texts.Clear();
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Add(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Add(command);
        return ValueTask.FromResult(result);
    }

    private void Add(DbCommand command)
    {
        lock (_texts)
        {
            _texts.Add(command.CommandText);
        }
    }
}
