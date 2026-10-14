using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Rask.Data.Tests;

/// <summary>
/// A refusal the model words as a sentence is the app's answer, so EF Core's Error entries for it — the failed
/// save (<c>Update[10000]</c>) and the failed command (<c>Database.Command[20102]</c>) — are not written. A
/// failure the model has no sentence for is logged exactly as before.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class QuietRefusalTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-quiet-{Guid.NewGuid():N}.db");
    private readonly CapturedLogs _logs = new();
    private readonly LoggerFactory _loggerFactory;

    public QuietRefusalTests() => _loggerFactory = new LoggerFactory([_logs]);

    public void Dispose()
    {
        _loggerFactory.Dispose();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_delete_refused_with_the_relationships_sentence_logs_no_error()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);

        await Assert.ThrowsAsync<RaskValidationException>(() => Trailer.Delete(trailer.Id, cancellationToken: Cancellation));

        Assert.Empty(_logs.Errors);
        Assert.Contains(_logs.Entries, static e => e is { Category: "Rask.Data", Level: LogLevel.Debug });
    }

    [Fact]
    public async Task A_delete_refused_with_the_entitys_sentence_logs_no_error()
    {
        await using var database = await StartDatabaseAsync();
        var tractor = Tractor.Plated("XYZ-789");
        await tractor.Save(cancellationToken: Cancellation);
        await database.Context.Database.ExecuteSqlAsync(
            $"""
             CREATE TABLE "TripLog" ("Id" INTEGER PRIMARY KEY, "TractorId" INTEGER NOT NULL REFERENCES "Tractor" ("Id"));
             INSERT INTO "TripLog" ("TractorId") VALUES ({tractor.Id});
             """,
            Cancellation);

        await Assert.ThrowsAsync<RaskValidationException>(() => Tractor.Delete(tractor.Id, cancellationToken: Cancellation));

        Assert.Empty(_logs.Errors);
    }

    [Fact]
    public async Task A_delete_refused_in_a_context_tracking_the_referencing_row_logs_no_error()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);
        await database.Context.Set<Docket>().LoadAsync(Cancellation);

        await Assert.ThrowsAsync<RaskValidationException>(
            () => Trailer.Delete(trailer.Id, db: database.Context, cancellationToken: Cancellation));

        Assert.Empty(_logs.Errors);
    }

    [Fact]
    public async Task A_unique_violation_with_a_message_logs_no_error()
    {
        await using var database = await StartDatabaseAsync();
        await Badge.Numbered("SN-1").Save(cancellationToken: Cancellation);

        await Assert.ThrowsAsync<RaskValidationException>(() => Badge.Numbered("SN-1").Save(cancellationToken: Cancellation));

        Assert.Empty(_logs.Errors);
    }

    [Fact]
    public async Task A_delete_refused_without_a_sentence_still_logs_the_failed_command_and_then_the_failed_save()
    {
        await using var database = await StartDatabaseAsync();
        var garage = Garage.Named("North");
        await garage.Save(cancellationToken: Cancellation);
        await Bay.In(garage.Id).Save(cancellationToken: Cancellation);

        await Assert.ThrowsAsync<DbUpdateException>(() => Garage.Delete(garage.Id, cancellationToken: Cancellation));

        Assert.Equal(
            [
                ("Microsoft.EntityFrameworkCore.Database.Command", RelationalEventId.CommandError.Id),
                ("Microsoft.EntityFrameworkCore.Update", CoreEventId.SaveChangesFailed.Id),
            ],
            _logs.Errors.Select(static e => (e.Category, e.Id.Id)));
    }

    [Fact]
    public async Task A_save_after_a_quiet_refusal_logs_its_own_untranslated_failure()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);
        await Assert.ThrowsAsync<RaskValidationException>(() => Trailer.Delete(trailer.Id, cancellationToken: Cancellation));

        await Assert.ThrowsAsync<DbUpdateException>(() => Docket.For(404).Save(cancellationToken: Cancellation));

        Assert.Equal(2, _logs.Errors.Count);
        Assert.Contains(_logs.Errors, static e => e.Id.Id == CoreEventId.SaveChangesFailed.Id);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").UseLoggerFactory(_loggerFactory));
}
