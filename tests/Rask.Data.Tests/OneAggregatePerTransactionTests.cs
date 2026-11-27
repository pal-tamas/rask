using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rask.Data.Tests;

// An aggregate whose key the store makes, so a save names it only once the key is made; with a child.
public sealed class Cellar : Aggregate<int>
{
    private readonly List<CellarRack> _bins = [];

    private Cellar() { }

    public string Name { get; private set; } = "";

    public IReadOnlyCollection<CellarRack> Bins => _bins;

    public static Cellar Named(string name) => new() { Name = name };

    public void Rename(string name) => Name = name;

    public void Fill(string grain) => _bins.Add(CellarRack.Of(grain));
}

public sealed class CellarRack : Entity<int>
{
    private CellarRack() { }

    public string Grain { get; private set; } = "";

    public void Refill(string grain) => Grain = grain;

    internal static CellarRack Of(string grain) => new() { Grain = grain };
}

// Plumbing the app keeps beside its aggregates: it says so, and a transaction writing one is still about one aggregate.
public sealed class CellarNote : Aggregate<Guid>, IInfrastructure
{
    private CellarNote() { }

    public string Text { get; private set; } = "";

    public static CellarNote Of(string text) => new() { Id = Guid.NewGuid(), Text = text };
}

/// <summary>
///     <c>OneAggregatePerTransaction</c>: a <c>Db.Transaction</c> that writes a second aggregate says why, with
///     <c>manyAggregates:</c> — a test database refuses one that does not.
/// </summary>
public sealed class OneAggregatePerTransactionTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_aggregates_without_a_reason_are_refused_and_nothing_is_written()
    {
        await using var database = await StartAsync();

        var refused = await Assert.ThrowsAsync<ManyAggregatesException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: Token);
        }, cancellationToken: Token));

        Assert.Equal(["Widget", "Supplier"], refused.Aggregates.Select(static a => a.Split(' ')[0]));
        Assert.Contains("manyAggregates: \"why\"", refused.Message, StringComparison.Ordinal);
        Assert.Equal((0, 0), (await Widget.Count(Token), await Supplier.Count(Token)));
    }

    [Fact]
    public async Task Two_aggregates_with_a_reason_commit_together()
    {
        await using var database = await StartAsync();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Widget.Create("hammer"), cancellationToken: Token);
        }, manyAggregates: "A pair is sold as one", cancellationToken: Token);

        Assert.Equal(2, await Widget.Count(Token));
    }

    [Fact]
    public async Task A_reason_on_a_transaction_that_returns_a_value_hands_the_value_back()
    {
        await using var database = await StartAsync();

        var count = await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Widget.Create("hammer"), cancellationToken: Token);
            return await Widget.Count(Token);
        }, manyAggregates: "A pair is sold as one", cancellationToken: Token);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task An_empty_reason_is_refused_before_anything_runs()
    {
        var ran = false;

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => Db.Transaction(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }, manyAggregates: " ", cancellationToken: Token));

        Assert.Equal("manyAggregates", refused.ParamName);
        Assert.False(ran);
    }

    [Fact]
    public async Task One_aggregate_created_then_saved_again_is_one()
    {
        await using var database = await StartAsync();

        await Db.Transaction(async () =>
        {
            var silo = await GeneratedModelWrites.Create(Cellar.Named("north"), cancellationToken: Token);
            var found = (await GeneratedModelWrites.Find<Cellar>(silo.Id, Token))!;
            found.Rename("north-east");
            found.Fill("wheat");
            await GeneratedModelWrites.Save(found, cancellationToken: Token);
        }, cancellationToken: Token);

        Assert.Equal(1, await Cellar.Count(Token));
    }

    [Fact]
    public async Task A_child_changed_on_its_own_counts_as_its_aggregate()
    {
        await using var database = await StartAsync();
        var silo = Cellar.Named("north");
        silo.Fill("wheat");
        await GeneratedModelWrites.Save(silo, cancellationToken: Token);

        await Db.Transaction(async () =>
        {
            var found = (await GeneratedModelWrites.Find<Cellar>(silo.Id, Token))!;
            found.Rename("north-east");
            await GeneratedModelWrites.Save(found, cancellationToken: Token);

            var again = (await GeneratedModelWrites.Find<Cellar>(silo.Id, Token))!;
            again.Bins.Single().Refill("rye");
            await GeneratedModelWrites.Save(again, cancellationToken: Token);
        }, cancellationToken: Token);

        Assert.Equal("rye", (await GeneratedModelWrites.Find<Cellar>(silo.Id, Token))!.Bins.Single().Grain);
    }

    [Fact]
    public async Task A_table_marked_infrastructure_is_not_a_second_aggregate()
    {
        await using var database = await StartAsync();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await GeneratedModelWrites.Create(CellarNote.Of("anvil made"), cancellationToken: Token);
        }, cancellationToken: Token);

        Assert.Equal(1, await Widget.Count(Token));
    }

    [Fact]
    public async Task A_nested_transactions_reason_covers_the_outer_one()
    {
        await using var database = await StartAsync();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await Db.Transaction(
                () => GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: Token),
                manyAggregates: "The supplier comes with its first widget",
                cancellationToken: Token);
        }, cancellationToken: Token);

        Assert.Equal((1, 1), (await Widget.Count(Token), await Supplier.Count(Token)));
    }

    [Fact]
    public async Task Off_lets_two_aggregates_commit_without_a_reason()
    {
        await using var database = await StartAsync(static o => o.OneAggregatePerTransaction = AggregatePolicy.Off);

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: Token);
        }, cancellationToken: Token);

        Assert.Equal((1, 1), (await Widget.Count(Token), await Supplier.Count(Token)));
    }

    [Fact]
    public async Task Warn_commits_and_logs_the_aggregates_once()
    {
        var log = new WarningLog();
        await using var database = await StartAsync(
            static o => o.OneAggregatePerTransaction = AggregatePolicy.Warn,
            services => services.AddSingleton<ILoggerProvider>(log));

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Supplier.In("AT"), cancellationToken: Token);
        }, cancellationToken: Token);

        Assert.Equal(3, await Widget.Count(Token) + await Supplier.Count(Token));
        Assert.Contains("writes 2 aggregates", Assert.Single(log.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recording_names_each_committed_transaction_and_what_it_wrote()
    {
        await using var database = await StartAsync();
        using var saves = Db.Record();

        await Db.Transaction(() => GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token), cancellationToken: Token);
        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("hammer"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: Token);
        }, manyAggregates: "A pair is sold as one", cancellationToken: Token);

        Assert.Equal([1, 2], saves.Transactions.Select(static t => t.Aggregates.Count));
        Assert.Equal([null, "A pair is sold as one"], saves.Transactions.Select(static t => t.ManyAggregates));
    }

    [Fact]
    public async Task Each_wrote_one_aggregate_passes_for_one_and_names_the_transaction_that_wrote_two()
    {
        await using var database = await StartAsync();
        using var saves = Db.Record();
        await Db.Transaction(() => GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token), cancellationToken: Token);
        saves.EachWroteOneAggregate();

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("hammer"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: Token);
        }, manyAggregates: "A pair is sold as one", cancellationToken: Token);
        var refused = Assert.Throws<ManyAggregatesException>(saves.EachWroteOneAggregate);

        Assert.Contains("A pair is sold as one", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rolled_back_transaction_and_one_after_the_recording_ends_are_not_recorded()
    {
        await using var database = await StartAsync();
        var saves = Db.Record();

        await Assert.ThrowsAsync<ManyAggregatesException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
            await GeneratedModelWrites.Create(Supplier.In("HU"), cancellationToken: Token);
        }, cancellationToken: Token));
        saves.Dispose();
        await Db.Transaction(() => GeneratedModelWrites.Create(Widget.Create("hammer"), cancellationToken: Token), cancellationToken: Token);

        Assert.Empty(saves.Transactions);
    }

    public void Dispose() => _sqlite.Dispose();

    private Task<TestDatabase> StartAsync(
        Action<RaskDataOptions>? policy = null, Action<IServiceCollection>? services = null)
    {
        return TestDatabase.Start(
            o => o.UseSqlite(_sqlite.ConnectionString),
            services: s =>
            {
                if (policy is not null)
                {
                    s.Configure(policy);
                }

                services?.Invoke(s);
            },
            cancellationToken: Token);
    }

    private sealed class WarningLog : ILoggerProvider, ILogger
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning && formatter(state, exception).Contains("aggregates", StringComparison.Ordinal))
            {
                Warnings.Add(formatter(state, exception));
            }
        }

        public void Dispose()
        {
        }
    }
}
