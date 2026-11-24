using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Rask.Data;

/// <summary>
///     Counts the aggregates each save inside a <c>Db.Transaction</c> writes, and meets a second one written without
///     <c>manyAggregates:</c> as <see cref="RaskDataOptions.OneAggregatePerTransaction" /> says: a warning, a
///     <see cref="ManyAggregatesException" />, or nothing.
/// </summary>
/// <remarks>
///     Before the save, so a refusal writes nothing; a row whose key the store makes is named after it, and refused then
///     — still inside the transaction, which the refusal rolls back. Outside a <c>Db.Transaction</c>, and at
///     <see cref="AggregatePolicy.Off" />, it reads one <see cref="AsyncLocal{T}" /> and returns.
/// </remarks>
internal sealed partial class OneAggregatePerTransactionInterceptor(
    IOptions<RaskDataOptions> options, ILoggerFactory? loggers = null)
    : SaveChangesInterceptor
{
    private readonly AggregatePolicy _policy = options.Value.OneAggregatePerTransaction;

    // Optional: a hand-wired container without AddLogging still saves, and only loses the warning.
    private readonly ILogger _logger =
        loggers?.CreateLogger<OneAggregatePerTransactionInterceptor>() ?? NullLogger<OneAggregatePerTransactionInterceptor>.Instance;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (Counting(eventData.Context) is { } transaction && transaction.Written.Stage(eventData.Context!))
        {
            Meet(transaction);
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        new(SavingChanges(eventData, result));

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (Counting(eventData.Context) is { } transaction && transaction.Written.Settle(eventData.Context!))
        {
            Meet(transaction);
        }

        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
        new(SavedChanges(eventData, result));

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (Counting(eventData.Context) is { } transaction)
        {
            transaction.Written.Abandon(eventData.Context!);
        }
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    // The transaction the save is part of, while a second aggregate in it would still owe a reason.
    private AmbientTransaction? Counting(DbContext? context) =>
        _policy != AggregatePolicy.Off && AmbientTransaction.Of(context) is { ManyAggregates: null } transaction
            ? transaction
            : null;

    private void Meet(AmbientTransaction transaction)
    {
        var written = transaction.Written;
        if (written.All.Count < 2)
        {
            return;
        }

        if (_policy == AggregatePolicy.Throw)
        {
            throw new ManyAggregatesException([.. written.All]);
        }

        if (!written.Warned)
        {
            written.Warned = true;
            LogManyAggregates(ManyAggregatesException.MessageFor(written.All));
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{Refusal}")]
    private partial void LogManyAggregates(string refusal);
}
