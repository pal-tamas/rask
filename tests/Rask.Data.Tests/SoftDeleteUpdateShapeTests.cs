using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

// #1055: a soft delete is an UPDATE of DeletedAt, not of the whole row. Turning the Deleted entry into Modified marked
// every property modified, so the statement wrote back each column the deleting context had loaded — and a delete of a
// row someone had changed since silently reverted their change.
[Collection("data-db")]
public sealed class SoftDeleteUpdateShapeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-softdelete-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_stale_delete_does_not_revert_a_change_made_after_it_loaded_the_row()
    {
        await using (var setup = Context<Card>())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(new Card { Id = "c1", Title = "original" });
            await setup.SaveChangesAsync();
        }

        await using var deleting = Context<Card>();
        var stale = await deleting.Set<Card>().SingleAsync(c => c.Id == "c1");

        await using (var renaming = Context<Card>())
        {
            (await renaming.Set<Card>().SingleAsync(c => c.Id == "c1")).Title = "renamed";
            await renaming.SaveChangesAsync();
        }

        deleting.Remove(stale);
        await deleting.SaveChangesAsync();

        await using var reading = Context<Card>();
        var row = await reading.Set<Card>().IgnoreQueryFilters().SingleAsync(c => c.Id == "c1");
        Assert.Equal("renamed", row.Title);
        Assert.NotNull(await reading.Set<Card>().IgnoreQueryFilters()
            .Select(c => EF.Property<DateTime?>(c, Columns.DeletedAt))
            .SingleAsync());
    }

    [Fact]
    public async Task The_soft_delete_UPDATE_sets_DeletedAt_and_the_audit_columns_and_nothing_else()
    {
        await using (var setup = Context<Ticket>())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(new Ticket { Id = "t1", Subject = "subject", Body = "body" });
            await setup.SaveChangesAsync();
        }

        var commands = new CommandCapture();
        await using var deleting = Context<Ticket>(commands);
        deleting.Remove(await deleting.Set<Ticket>().SingleAsync(t => t.Id == "t1"));
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

    // Soft-deletable, not versioned: nothing but the statement's shape protects a concurrent change here.
    private sealed class Card : ISoftDeletable
    {
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";
    }

    // All three markers, so the pin covers the columns AuditingInterceptor adds to the narrowed statement.
    private sealed class Ticket : ITimestamped, ISoftDeletable, IVersioned
    {
        public string Id { get; set; } = "";

        public string Subject { get; set; } = "";

        public string Body { get; set; } = "";

        public int Version { get; set; }
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
