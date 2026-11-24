using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
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
///     What a save writes is noted before it and counted after it, so a save refused for another reason (a rule, a
///     referenced delete) counts nothing; a refusal comes after the save, still inside the transaction it rolls back. Outside a <c>Db.Transaction</c>, and at
///     <see cref="AggregatePolicy.Off" /> with no <see cref="Db.Record" /> open, it reads one <see cref="AsyncLocal{T}" /> and returns.
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
        Counting(eventData.Context)?.Written.Stage(eventData.Context!);
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

    /// <summary>Counts a set write of <paramref name="type" /> on <paramref name="context" />, and meets the policy as a save would.</summary>
    internal void CountSet(DbContext context, IEntityType type)
    {
        if (Counting(context) is { } transaction && transaction.Written.NoteSet(type))
        {
            Meet(transaction);
        }
    }

    // The transaction the save is part of, while what it writes is asked about: by the policy, or by a Db.Record().
    private AmbientTransaction? Counting(DbContext? context) =>
        AmbientTransaction.Of(context) is { } transaction && (transaction.Recorded || _policy != AggregatePolicy.Off)
            ? transaction
            : null;

    // A second aggregate meets the policy only while no reason was stated; a recording counts it either way.
    private void Meet(AmbientTransaction transaction)
    {
        var written = transaction.Written;
        if (written.All.Count < 2 || transaction.ManyAggregates is not null || _policy == AggregatePolicy.Off)
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
