using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Querying;

namespace Rask.Data.Tests;

/// <summary>
///     What Rask starts from inside a <c>Db.Transaction</c> — a render a <c>StateHasChanged</c> asks for, a handler's
///     repaint, a query's fetch, a subscription's stream — reads outside it: on a connection of its own, at the
///     database's default isolation, seeing only what is committed. Inherited, it ran the page's reads on the
///     transaction's connection at Serializable, and a read the app made only outside its transactions deadlocked.
/// </summary>
/// <remarks>
///     Each read here is a read face on the read context, and the block has written a consignment it has not
///     committed: a read that joined the transaction counts it, one outside does not.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class DbTransactionSpawnedWorkTests : IDisposable
{
    private static readonly Seen Outside = new(InTransaction: false, Consignments: 0);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-spawned-work-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_read_inside_the_block_still_sees_its_own_uncommitted_write()
    {
        await using var database = await StartDatabaseAsync();
        Seen? inside = null;

        await Db.Transaction(async () =>
        {
            await WriteUncommittedAsync(database);
            inside = await ReadAsync(TestContext.Current.CancellationToken);
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(new Seen(InTransaction: true, Consignments: 1), inside);
    }

    [Fact]
    public async Task A_render_a_state_change_asks_for_inside_the_block_reads_outside_it()
    {
        await using var database = await StartDatabaseAsync();
        using var session = new ReadingSession();

        await Db.Transaction(async () =>
        {
            await WriteUncommittedAsync(database);
            await session.RequestRender();
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(Outside, session.Rendered);
    }

    [Fact]
    public async Task A_handlers_repaint_between_its_awaits_inside_the_block_reads_outside_it()
    {
        await using var database = await StartDatabaseAsync();
        using var session = new ReadingSession();

        // The context a live handler runs under: each continuation it posts is repainted before it runs.
        var handler = new HandlerSyncContext(((IRenderHandle)session).RenderInScopeAsync);
        await Db.Transaction(async () =>
        {
            await WriteUncommittedAsync(database);
            handler.Post(static _ => { }, null);
            await handler.DrainAsync();
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(Outside, session.Repainted);
    }

    [Fact]
    public async Task A_query_fetch_started_inside_the_block_reads_outside_it()
    {
        await using var database = await StartDatabaseAsync();
        var client = new SessionQueryClient(new NothingToDispatch());
        Seen? fetched = null;

        await Db.Transaction(async () =>
        {
            await WriteUncommittedAsync(database);
            using var stock = client.Query(QueryKey.Of("stock"), ReadAsync);
            await stock.Reload(TestContext.Current.CancellationToken);
            fetched = stock.Data;
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(Outside, fetched);
    }

    [Fact]
    public async Task A_subscription_started_inside_the_block_reads_outside_it()
    {
        await using var database = await StartDatabaseAsync();
        var client = new SessionQueryClient(new NothingToDispatch());
        var pushed = new TaskCompletionSource<Seen>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Db.Transaction(async () =>
        {
            await WriteUncommittedAsync(database);
            using var feed = client.Subscribe(() => 1, (_, ct) => Stream(pushed, ct));
            _ = feed.Data; // opens at its first read, as a render's would
            await pushed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(Outside, await pushed.Task);
    }

    [Fact]
    public async Task Work_started_inside_the_block_leaves_the_block_holding_its_transaction()
    {
        await using var database = await StartDatabaseAsync();
        using var session = new ReadingSession();
        Seen? after = null;

        await Db.Transaction(async () =>
        {
            await WriteUncommittedAsync(database);
            await session.RequestRender();
            after = await ReadAsync(TestContext.Current.CancellationToken);
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(new Seen(InTransaction: true, Consignments: 1), after);
    }

    private static async Task<Seen> ReadAsync(CancellationToken cancellationToken) =>
        new(AmbientTransaction.Current is not null, await Consignment.Count(cancellationToken));

    private static async IAsyncEnumerable<Seen> Stream(
        TaskCompletionSource<Seen> pushed, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = await ReadAsync(cancellationToken);
        pushed.TrySetResult(seen);
        yield return seen;
    }

    private static async Task WriteUncommittedAsync(TestDatabase database)
    {
        var supplier = await GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: TestContext.Current.CancellationToken);
        await GeneratedModelWrites.Create(Consignment.For(supplier.Id, "C-1", 10m), cancellationToken: TestContext.Current.CancellationToken);
        GC.KeepAlive(database);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath};Default Timeout=5"));

    /// <summary>What a read saw: whether it ran in the transaction, and how many consignments it counted.</summary>
    private sealed record Seen(bool InTransaction, int Consignments);

    // A live session whose render and repaint are a page's read, and nothing else.
    private sealed class ReadingSession() : LiveSessionBase(new Blank(), new ServiceCollection().BuildServiceProvider(), LiveDiffMode.Auto)
    {
        public Seen? Rendered { get; private set; }

        public Seen? Repainted { get; private set; }

        protected override async Task RequestRenderInternalAsync(bool publishOnly) =>
            Rendered = await ReadAsync(CancellationToken.None);

        protected override async Task RenderInScopeCoreAsync() =>
            Repainted = await ReadAsync(CancellationToken.None);

        protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => default;

        // Nothing to tear down: no pooled buffer was ever rented.
        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class Blank : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class NothingToDispatch : IDispatcher
    {
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send(ICommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(
            ISubscription<TEvent> subscription, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();
    }
}
