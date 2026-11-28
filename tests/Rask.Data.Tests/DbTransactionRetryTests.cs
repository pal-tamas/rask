using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>
///     A <c>Db.Transaction</c> attempt that rolls back takes back what its saves changed on the objects they wrote, so the
///     retry saves them as if the failed attempt had never run — even an aggregate loaded or made before the block.
/// </summary>
public sealed class DbTransactionRetryTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose()
    {
        _sqlite.Dispose();
    }

    [Fact]
    public async Task An_aggregate_read_before_the_block_is_saved_once_with_the_right_version()
    {
        await using var database = await StartDatabaseAsync();
        var anvil = Widget.Create("anvil");
        await anvil.Save(cancellationToken: TestContext.Current.CancellationToken);
        var read = (await Widget.Find(anvil.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var versionRead = read.Version;
        var attempts = 0;

        read.Rename("hammer");
        await Db.Transaction(async () =>
        {
            attempts++;
            await read.Save(cancellationToken: TestContext.Current.CancellationToken);
            FailTheFirst(attempts);
        }, cancellationToken: TestContext.Current.CancellationToken);

        var stored = (await Widget.Find(anvil.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal(2, attempts);
        Assert.Equal("hammer", stored.Name);
        Assert.Equal(versionRead + 1, stored.Version);
        Assert.Equal(stored.Version, read.Version);
    }

    [Fact]
    public async Task A_new_aggregate_made_before_the_block_is_inserted_once_with_its_children()
    {
        await using var database = await StartDatabaseAsync();
        var logbook = Logbook.For("ship").Add("sailed");
        var attempts = 0;

        await Db.Transaction(async () =>
        {
            attempts++;
            await logbook.Save(cancellationToken: TestContext.Current.CancellationToken);
            FailTheFirst(attempts);
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var read = Db.CreateOwnContext();
        var stored = await read.Set<Logbook>().Include(l => l.Entries).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, attempts);
        Assert.Equal(stored.Id, logbook.Id);
        Assert.Equal(["sailed"], stored.Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task A_change_saved_again_after_the_rollback_is_written_and_a_later_save_is_not_a_conflict()
    {
        await using var database = await StartDatabaseAsync();
        var anvil = Widget.Create("anvil");
        await anvil.Save(cancellationToken: TestContext.Current.CancellationToken);

        anvil.Rename("hammer");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await anvil.Save(cancellationToken: TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));
        await anvil.Save(cancellationToken: TestContext.Current.CancellationToken);
        anvil.Rename("tongs");
        await anvil.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = (await Widget.Find(anvil.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal("tongs", stored.Name);
    }

    [Fact]
    public async Task The_events_of_an_aggregate_made_before_the_block_are_published_once_after_the_retry()
    {
        var events = new EventRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(events);
        services.AddRaskCqrs();
        services.AddRaskData<TestDbContext>();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite(_sqlite.ConnectionString, s => s.ExecutionStrategy(d => new RetryOnTimeout(d)))
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(_sqlite.ConnectionString));
        await using var provider = services.BuildServiceProvider();
        await using (var db = await provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        using var model = Db.Use(provider);
        await using var session = provider.CreateAsyncScope();
        using var bound = Db.UseScope(session.ServiceProvider);
        var anvil = Widget.Create("anvil");
        var attempts = 0;

        await Db.Transaction(async () =>
        {
            attempts++;
            await GeneratedModelWrites.Create(anvil, cancellationToken: TestContext.Current.CancellationToken);
            FailTheFirst(attempts);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Equal(new WidgetCreated(anvil.Id), Assert.Single(events.Events));
        Assert.Empty(anvil.DomainEvents);
    }

    [Fact]
    public async Task A_serialization_failure_on_the_save_is_retried_rather_than_refused_as_a_stale_row()
    {
        var losing = new LoseTheFirstSave();
        await using var database = await TestDatabase.Start(o => o
            .UseSqlite(_sqlite.ConnectionString, s => s.ExecutionStrategy(d => new RetryOnTransient(d)))
            .AddInterceptors(losing),
            cancellationToken: TestContext.Current.CancellationToken);
        var anvil = Widget.Create("anvil");
        var attempts = 0;

        await Db.Transaction(async () =>
        {
            attempts++;
            await GeneratedModelWrites.Create(anvil, cancellationToken: TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        var stored = await Widget.Find(anvil.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, attempts);
        Assert.Equal("anvil", stored?.Name);
    }

    // The first attempt's saves are written, then a transient failure rolls it back and the strategy runs it again.
    private static void FailTheFirst(int attempt)
    {
        if (attempt == 1)
        {
            throw new TimeoutException("transient");
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(
            _sqlite.ConnectionString, s => s.ExecutionStrategy(d => new RetryOnTimeout(d))),
            cancellationToken: TestContext.Current.CancellationToken);

    private sealed class RetryOnTimeout(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 2, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
    }

    // Npgsql's spelling of PostgreSQL's 40001: a concurrency exception over a provider error that says retry me.
    private sealed class LoseTheFirstSave : SaveChangesInterceptor
    {
        private int _saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _saves) == 1
                ? throw new DbUpdateConcurrencyException("could not serialize access", new SerializationFailure())
                : ValueTask.FromResult(result);
    }

    private sealed class SerializationFailure() : DbException("40001: could not serialize access")
    {
        public override bool IsTransient => true;
    }

    private sealed class RetryOnTransient(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 2, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is DbException { IsTransient: true };
    }
}
