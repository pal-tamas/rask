using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>One parent of a boarding pass: a pass is held by a ferryman OR a ferry, never both.</summary>
public sealed class Ferryman : Aggregate<string, Versioned, Model, Deletable>
{
    private readonly List<BoardingPass> _passes = [];

    private Ferryman() { }

    public IReadOnlyList<BoardingPass> Passes => _passes;

    public static Ferryman Issued(string id, string pass)
    {
        var ferryman = new Ferryman { Id = id };
        ferryman._passes.Add(BoardingPass.Of(pass));
        return ferryman;
    }

    public void Relabel(string pass, string label) => _passes.Single(p => p.Id == pass).Relabel(label);

    public void HandOver(string pass, string ferry) => _passes.Single(p => p.Id == pass).MoveTo(ferry);
}

/// <summary>The other parent of a boarding pass. Its key sorts first, so it is the first parent key of the pass.</summary>
public sealed class Ferry : Aggregate<string, Model, Deletable>
{
    private readonly List<BoardingPass> _passes = [];

    private Ferry() { }

    public IReadOnlyList<BoardingPass> Passes => _passes;

    public static Ferry Built(string id, string pass)
    {
        var ferry = new Ferry { Id = id };
        ferry._passes.Add(BoardingPass.Of(pass));
        return ferry;
    }

    public void Relabel(string pass, string label) => _passes.Single(p => p.Id == pass).Relabel(label);

    public void Drop(string pass) => _passes.Remove(_passes.Single(p => p.Id == pass));

    public void Share(string pass, string ferryman) => _passes.Single(p => p.Id == pass).AlsoHeldBy(ferryman);
}

/// <summary>A child of two aggregates, holding one parent's key and NULL in the other (TA's Chip under a Driver or a Vehicle).</summary>
public sealed class BoardingPass : Entity<string>
{
    private BoardingPass() { }

    public string? FerryId { get; private set; }

    public string? FerrymanId { get; private set; }

    public string Label { get; private set; } = "";

    internal static BoardingPass Of(string id) => new() { Id = id };

    internal void Relabel(string label) => Label = label;

    internal void MoveTo(string ferry) => (FerrymanId, FerryId) = (null, ferry);

    internal void AlsoHeldBy(string ferryman) => FerrymanId = ferryman;
}

/// <summary>
///     A child two aggregates may hold is counted, touched and reported as the aggregate its ROW is held by — never as
///     whichever parent its type declares first, which names a parent with no key for every row of the other one.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class TwoParentChildTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly SavedTypes _saved = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task A_ferry_saved_with_its_pass_in_a_transaction_is_one_aggregate()
    {
        await using var database = await StartAsync();
        using var saves = Db.Record();

        await Db.Transaction(() => GeneratedModelWrites.Create(Ferry.Built("f1", "p1"), cancellationToken: Token), cancellationToken: Token);

        Assert.Equal(["Ferry f1"], Assert.Single(saves.Transactions).Aggregates);
    }

    [Fact]
    public async Task A_ferryman_saved_with_its_pass_in_a_transaction_is_one_aggregate()
    {
        await using var database = await StartAsync();
        await GeneratedModelWrites.Create(Ferryman.Issued("m1", "p1"), cancellationToken: Token);
        using var saves = Db.Record();

        await Db.Transaction(async () =>
        {
            var ferryman = (await GeneratedModelWrites.Find<Ferryman>("m1", Token))!;
            ferryman.Relabel("p1", "deck");
            await GeneratedModelWrites.Save(ferryman, cancellationToken: Token);
        }, cancellationToken: Token);

        Assert.Equal(["Ferryman m1"], Assert.Single(saves.Transactions).Aggregates);
    }

    [Fact]
    public async Task A_pass_moved_from_a_ferryman_to_a_ferry_lands_on_the_ferry_and_writes_both()
    {
        await using var database = await StartAsync();
        await GeneratedModelWrites.Create(Ferryman.Issued("m1", "p1"), cancellationToken: Token);
        await GeneratedModelWrites.Create(Ferry.Built("f1", "p2"), cancellationToken: Token);
        using var saves = Db.Record();

        await Db.Transaction(async () =>
        {
            var ferryman = (await GeneratedModelWrites.Find<Ferryman>("m1", Token))!;
            ferryman.HandOver("p1", "f1");
            await GeneratedModelWrites.Save(ferryman, cancellationToken: Token);
        }, manyAggregates: "A pass changes hands", cancellationToken: Token);

        Assert.Equal(["Ferry f1", "Ferryman m1"], Assert.Single(saves.Transactions).Aggregates.Order(StringComparer.Ordinal));
        Assert.Equal(["p1", "p2"], (await GeneratedModelWrites.Find<Ferry>("f1", Token))!.Passes.Select(static p => p.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_pass_dropped_from_its_ferry_and_a_ferry_deleted_with_its_pass_are_each_the_ferry()
    {
        await using var database = await StartAsync();
        await GeneratedModelWrites.Create(Ferry.Built("f1", "p1"), cancellationToken: Token);
        await GeneratedModelWrites.Create(Ferry.Built("f2", "p2"), cancellationToken: Token);
        using var saves = Db.Record();

        await Db.Transaction(async () =>
        {
            var ferry = (await GeneratedModelWrites.Find<Ferry>("f1", Token))!;
            ferry.Drop("p1");
            await GeneratedModelWrites.Save(ferry, cancellationToken: Token);
        }, cancellationToken: Token);
        await Db.Transaction(
            () => GeneratedModelWrites.Delete<Ferry>("f2", version: null, cancellationToken: Token), cancellationToken: Token);

        Assert.Equal([["Ferry f1"], ["Ferry f2"]], saves.Transactions.Select(static t => t.Aggregates.ToList()));
        Assert.Equal(0, await BoardingPass.Count(Token));
    }

    [Fact]
    public async Task A_pass_both_parents_hold_is_both_aggregates()
    {
        await using var database = await StartAsync();
        await GeneratedModelWrites.Create(Ferryman.Issued("m1", "p1"), cancellationToken: Token);
        await GeneratedModelWrites.Create(Ferry.Built("f1", "p2"), cancellationToken: Token);

        var refused = await Assert.ThrowsAsync<ManyAggregatesException>(() => Db.Transaction(async () =>
        {
            var ferry = (await GeneratedModelWrites.Find<Ferry>("f1", Token))!;
            ferry.Share("p2", "m1");
            await GeneratedModelWrites.Save(ferry, cancellationToken: Token);
        }, cancellationToken: Token));

        Assert.Equal(["Ferry f1", "Ferryman m1"], refused.Aggregates);
    }

    [Fact]
    public async Task A_pass_changed_on_a_ferryman_touches_and_reports_the_ferryman()
    {
        await using var database = await StartAsync();
        await GeneratedModelWrites.Create(Ferryman.Issued("m1", "p1"), cancellationToken: Token);
        _saved.Types.Clear();

        var ferryman = (await GeneratedModelWrites.Find<Ferryman>("m1", Token))!;
        ferryman.Relabel("p1", "deck");
        await GeneratedModelWrites.Save(ferryman, cancellationToken: Token);

        Assert.Equal([typeof(Ferryman)], _saved.Types);
        Assert.Equal(1, (await GeneratedModelWrites.Find<Ferryman>("m1", Token))!.Version);
    }

    private Task<TestDatabase> StartAsync() =>
        TestDatabase.Start(
            o => o.UseSqlite(_sqlite.ConnectionString),
            services: s => s.AddSingleton<ISavedTypeWatcher>(_saved),
            cancellationToken: Token);

    private sealed class SavedTypes : ISavedTypeWatcher
    {
        public List<Type> Types { get; } = [];

        public bool Watches(Type aggregate) => true;

        public void Saved(IReadOnlyCollection<Type> aggregates)
        {
            lock (Types)
            {
                Types.AddRange(aggregates);
            }
        }
    }
}
