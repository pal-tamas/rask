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

    [Fact]
    public async Task An_untranslated_failure_reaches_the_apps_logger_in_the_scope_it_was_logged_in()
    {
        await using var database = await StartDatabaseAsync();
        var app = _loggerFactory.CreateLogger("App");

        using (app.BeginScope("request-42"))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Docket.For(404).Save(cancellationToken: Cancellation));
        }

        Assert.Equal(
            [RelationalEventId.CommandError.Id, CoreEventId.SaveChangesFailed.Id],
            _logs.Errors.Select(static e => e.Id.Id));
        Assert.All(_logs.Errors, static e => Assert.Contains("request-42", e.Scopes));
    }

    [Fact]
    public async Task The_rest_of_a_quiet_refusals_save_is_logged_in_its_scope()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);
        var app = _loggerFactory.CreateLogger("App");

        using (app.BeginScope("request-43"))
        {
            await Assert.ThrowsAsync<RaskValidationException>(
                () => Trailer.Delete(trailer.Id, cancellationToken: Cancellation));
        }

        Assert.Empty(_logs.Errors);
        Assert.Contains(
            _logs.Entries,
            static e => e.Category == "Microsoft.EntityFrameworkCore.Database.Command" && e.Scopes.Contains("request-43"));
    }

    [Fact]
    public async Task A_failed_query_is_another_ef_error_and_is_still_logged()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAnyAsync<Exception>(
            () => database.Context.Database.SqlQueryRaw<int>("SELECT \"Nope\" FROM \"Missing\"").ToListAsync(Cancellation));

        Assert.Contains(_logs.Errors, static e => e.Id.Id == RelationalEventId.CommandError.Id);
        Assert.Contains(_logs.Errors, static e => e.Id.Id == CoreEventId.QueryIterationFailed.Id);
    }

    [Fact]
    public async Task An_apps_own_error_in_ef_cores_categories_is_untouched()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Trailer.Delete(trailer.Id, cancellationToken: Cancellation));

        var update = _loggerFactory.CreateLogger("Microsoft.EntityFrameworkCore.Update");
        var command = _loggerFactory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
#pragma warning disable CA1848, CA2254 // the test logs EF Core's own event ids by hand, as an app could
        update.Log(LogLevel.Error, CoreEventId.SaveChangesFailed, refused.InnerException, "The app's own line.");
        command.Log(LogLevel.Error, RelationalEventId.CommandError, refused.InnerException, "The app's own line.");
#pragma warning restore CA1848, CA2254

        Assert.Equal(2, _logs.Errors.Count);
        Assert.All(_logs.Errors, e => Assert.Same(refused.InnerException, e.Exception));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}").UseLoggerFactory(_loggerFactory));
}
