using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>
///     <c>Db.Transaction</c> on a hand-wired <c>AddRaskData&lt;TContext&gt;()</c> host: what a save announces — its
///     domain events, the data-changed notice to the session — goes out after the commit, and never after a rollback.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DbTransactionHostTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-transaction-host-{Guid.NewGuid():N}.db");
    private readonly EventRecorder _events = new();
    private readonly ChangeRecorder _changes = new();
    private readonly ServiceProvider _provider;

    public DbTransactionHostTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_events);
        services.AddScoped<IDataChanges>(_ => _changes);
        services.AddRaskCqrs();
        services.AddRaskData<TestDbContext>();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));

        _provider = services.BuildServiceProvider();
        using var db = _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        Db.Configure(_provider);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _provider.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task The_events_and_the_change_notice_wait_for_the_commit()
    {
        await using var session = _provider.CreateAsyncScope();
        using var bound = Db.UseScope(session.ServiceProvider);
        int eventsBeforeCommit = -1, changesBeforeCommit = -1;

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
            (eventsBeforeCommit, changesBeforeCommit) = (_events.Events.Count, _changes.Saves.Count);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((0, 0), (eventsBeforeCommit, changesBeforeCommit));
        Assert.IsType<WidgetCreated>(Assert.Single(_events.Events));
        Assert.Equal([typeof(Widget)], Assert.Single(_changes.Saves));
    }

    [Fact]
    public async Task A_rollback_announces_nothing()
    {
        await using var session = _provider.CreateAsyncScope();
        using var bound = Db.UseScope(session.ServiceProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(_events.Events);
        Assert.Empty(_changes.Saves);
        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_delete_of_a_found_aggregate_announces_its_events_only_after_the_commit()
    {
        await using var session = _provider.CreateAsyncScope();
        using var bound = Db.UseScope(session.ServiceProvider);
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var eventsBefore = _events.Events.Count;
        var eventsBeforeCommit = -1;

        await Db.Transaction(async () =>
        {
            found.MarkDeleted();
            await found.Delete(cancellationToken: TestContext.Current.CancellationToken);
            eventsBeforeCommit = _events.Events.Count - eventsBefore;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, eventsBeforeCommit);
        Assert.IsType<WidgetDeleted>(_events.Events[^1]);
        Assert.Equal(0, await Widget.Count(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_delete_of_a_found_aggregate_rolled_back_announces_nothing_and_stamps_nothing()
    {
        await using var session = _provider.CreateAsyncScope();
        using var bound = Db.UseScope(session.ServiceProvider);
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var eventsBefore = _events.Events.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            found.MarkDeleted();
            await found.Delete(cancellationToken: TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(eventsBefore, _events.Events.Count);
        Assert.Equal(1, await Widget.Count(TestContext.Current.CancellationToken));
    }

    private sealed class ChangeRecorder : IDataChanges
    {
        public List<Type[]> Saves { get; } = [];

        public void Saved(IReadOnlyCollection<Type> entityTypes) => Saves.Add([.. entityTypes]);
    }
}
