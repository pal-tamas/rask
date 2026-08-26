using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Translates the SQLite error raised by a non-overlapping range trigger into a
/// <see cref="RangeOverlapException"/>, so callers match on a Rask type instead of a store error code.
/// </summary>
/// <remarks>
/// A rejected booking is an expected outcome of a booking screen, not a fault, and
/// <c>SQLITE_CONSTRAINT_TRIGGER</c> buried two levels inside a <c>DbUpdateException</c> is a poor thing to ask
/// application code to match on. Every other failure is left exactly as it was.
/// </remarks>
public sealed class RaskSqliteRangeExclusionInterceptor : ISaveChangesInterceptor
{
    // SQLITE_CONSTRAINT_TRIGGER: the extended result code for a RAISE(ABORT) inside a trigger.
    private const int ConstraintTrigger = 1811;

    // The tail of the message the generator emits, used to tell our triggers from any other the app defines.
    private const string Marker = ": range overlaps an existing row";

    /// <inheritdoc />
    public void SaveChangesFailed(DbContextErrorEventData eventData) => Translate(eventData);

    /// <inheritdoc />
    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Translate(eventData);
        return Task.CompletedTask;
    }

    private static void Translate(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Exception is not DbUpdateException update)
        {
            return;
        }

        if (update.InnerException is not SqliteException { SqliteExtendedErrorCode: ConstraintTrigger } sqlite)
        {
            return;
        }

        var index = sqlite.Message.IndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return;
        }

        // The RAISE text is "<table>: range overlaps an existing row", wrapped by Microsoft.Data.Sqlite as
        // "SQLite Error 19: '<text>'." — walk back to the quote to recover the table name.
        var start = sqlite.Message.LastIndexOf('\'', index) + 1;
        var table = start > 0 && start < index ? sqlite.Message[start..index] : string.Empty;

        throw RangeOverlapException.ForTable(table, update);
    }
}
