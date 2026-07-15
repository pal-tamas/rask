using Microsoft.Data.Sqlite;

namespace Rask.Benchmarks.Sqlite;

/// <summary>
/// Turns a thrown exception into an <see cref="OpOutcome"/>, identically for the raw and EF arms — otherwise
/// the two paths' error counts would not be comparable and the whole head-to-head would be meaningless.
/// </summary>
internal static class ErrorClassifier
{
    // SqliteException.SqliteErrorCode primary result codes, matching RaskSqliteExecutionStrategy's own
    // inlined constants.
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    /// <summary>
    /// Classifies <paramref name="exception"/>, walking the whole <see cref="Exception.InnerException"/>
    /// chain for a <see cref="SqliteException"/>.
    /// <para>
    /// The walk is mandatory, not defensive: EF Core wraps the provider error (a contended
    /// <c>SaveChanges</c> surfaces as <c>DbUpdateException</c> → <c>SqliteException</c>), so a plain
    /// <c>catch (SqliteException)</c> would miss <b>every</b> EF busy and report the EF arms as flawless.
    /// That is the single easiest way to make this report a lie.
    /// </para>
    /// </summary>
    internal static OpOutcome Classify(Exception exception, CancellationToken cancellationToken)
    {
        // A deadline hit mid-op is not a failure of the code under test. Its latency is truncated by the
        // deadline, so recording it would drag the tail down; the runner discards it.
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return new OpOutcome(OutcomeKind.Cancelled);
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite)
            {
                return sqlite.SqliteErrorCode is SqliteBusy or SqliteLocked
                    ? new OpOutcome(
                        OutcomeKind.Busy,
                        sqlite.SqliteErrorCode,
                        $"rc={sqlite.SqliteErrorCode}/ext={sqlite.SqliteExtendedErrorCode}: {sqlite.Message} :: {exception}")
                    : new OpOutcome(
                        OutcomeKind.SqliteError,
                        sqlite.SqliteErrorCode,
                        $"{nameof(SqliteException)}: {sqlite.Message}");
            }
        }

        // The message, not just the type: an unexpected failure here is the harness lying about what it
        // measured, and "InvalidOperationException" alone is not enough to find out why.
        return new OpOutcome(OutcomeKind.Other, 0, $"{exception.GetType().Name}: {exception.Message}");
    }
}
