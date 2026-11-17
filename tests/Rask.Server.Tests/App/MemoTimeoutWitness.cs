using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Server.Tests.App;

/// <summary>The command timeout <see cref="LedgerMemo"/>'s statement adding the memo column ran with.</summary>
internal sealed class MemoTimeoutWitness : DbCommandInterceptor
{
    public int? AddingTheMemo { get; private set; }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"Memo\"", StringComparison.Ordinal))
        {
            AddingTheMemo = command.CommandTimeout;
        }

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
