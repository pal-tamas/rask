using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

// Aggregates, so the conventions give them DeletedAt, the timestamps and Version. Namespace-level rather than nested:
// the generated model registry maps every aggregate in the assembly, and cannot name a private nested one.
public sealed class ShapeCard : Aggregate<string>
{
    // These tests are about soft delete, which is opt-in now.
    public const Deletion Deletes = Deletion.Soft;

    private ShapeCard() { }

    public string Title { get; private set; } = "";

    public static ShapeCard Draw(string id, string title) => new() { Id = id, Title = title };

    public void Retitle(string title) => Title = title;
}

public sealed class ShapeTicket : Aggregate<string>
{
    // These tests are about soft delete, which is opt-in now.
    public const Deletion Deletes = Deletion.Soft;

    private ShapeTicket() { }

    public string Subject { get; private set; } = "";

    public string Body { get; private set; } = "";

    public static ShapeTicket Open(string id, string subject, string body) => new() { Id = id, Subject = subject, Body = body };
}

// #1055: a soft delete is an UPDATE of DeletedAt, not of the whole row. Turning the Deleted entry into Modified marked
// every property modified, so the statement wrote back each column the deleting context had loaded — and a delete of a
// row someone had changed since silently reverted their change.
[Collection("data-db")]
public sealed class SoftDeleteUpdateShapeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-softdelete-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_stale_delete_is_refused_by_the_version_and_does_not_revert_a_change_made_after_it_loaded_the_row()
    {
        await using (var setup = Context<ShapeCard>())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(ShapeCard.Draw("c1", "original"));
            await setup.SaveChangesAsync();
        }

        await using var deleting = Context<ShapeCard>();
        var stale = await deleting.Set<ShapeCard>().SingleAsync(c => c.Id == "c1");

        await using (var renaming = Context<ShapeCard>())
        {
            (await renaming.Set<ShapeCard>().SingleAsync(c => c.Id == "c1")).Retitle("renamed");
            await renaming.SaveChangesAsync();
        }

        deleting.Remove(stale);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => deleting.SaveChangesAsync());

        await using var reading = Context<ShapeCard>();
        var row = await reading.Set<ShapeCard>().IgnoreQueryFilters().SingleAsync(c => c.Id == "c1");
        Assert.Equal("renamed", row.Title);
        Assert.Null(row.DeletedAt);
    }

    [Fact]
    public async Task The_soft_delete_UPDATE_sets_DeletedAt_and_the_audit_columns_and_nothing_else()
    {
        await using (var setup = Context<ShapeTicket>())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(ShapeTicket.Open("t1", "subject", "body"));
            await setup.SaveChangesAsync();
        }

        var commands = new CommandCapture();
        await using var deleting = Context<ShapeTicket>(commands);
        deleting.Remove(await deleting.Set<ShapeTicket>().SingleAsync(t => t.Id == "t1"));
        await deleting.SaveChangesAsync();

        var update = Assert.Single(commands.Texts, t => t.TrimStart().StartsWith("UPDATE ", StringComparison.Ordinal));
        var set = System.Text.RegularExpressions.Regex.Match(
            update, @"\bSET\b(?<columns>.*?)\bWHERE\b", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(set.Success, update);
        var setClause = set.Groups["columns"].Value;
        Assert.Contains("\"DeletedAt\"", setClause);
        Assert.Contains("\"UpdatedAt\"", setClause);
        Assert.Contains("\"Version\"", setClause);
        Assert.DoesNotContain("\"Subject\"", setClause);
        Assert.DoesNotContain("\"Body\"", setClause);
        Assert.DoesNotContain("\"CreatedAt\"", setClause);
    }

    private TestContext<T> Context<T>(CommandCapture? capture = null)
        where T : class
    {
        var builder = new DbContextOptionsBuilder<TestContext<T>>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .AddInterceptors(new SoftDeleteInterceptor(TimeProvider.System), new AuditingInterceptor(TimeProvider.System));
        if (capture is not null)
        {
            builder.AddInterceptors(capture);
        }

        return new TestContext<T>(builder.Options);
    }

    private sealed class TestContext<T>(DbContextOptions<TestContext<T>> options) : DbContext(options)
        where T : class
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<T>();
            modelBuilder.ApplyRaskConventions();
        }
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Texts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Texts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
