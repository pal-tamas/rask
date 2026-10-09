using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>The shape of a table Rask did not create: an identity key, a name of its own, and no framework columns.</summary>
public sealed class Depot : Aggregate<int>
{
    private readonly List<DepotBay> _bays = [];

    private Depot() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public string Name { get; private set; } = "";

    public IReadOnlyCollection<DepotBay> Bays => _bays;

    public static Depot Named(string name) => new() { Name = name };

    public void Rename(string name) => Name = name;

    public DepotBay AddBay(string label)
    {
        var bay = DepotBay.Labelled(label);
        _bays.Add(bay);
        return bay;
    }

    public static void Configure(EntityTypeBuilder<Depot> builder)
    {
        builder.ToTable("Depots");
        builder.Property(d => d.Name).IsRequired().HasMaxLength(255);
    }
}

/// <summary>A child of an aggregate with nothing to touch: no stamps of its own either.</summary>
public sealed class DepotBay : Entity<int>
{
    private DepotBay() { }

    public const Timestamps Stamps = Timestamps.None;

    public string Label { get; private set; } = "";

    internal static DepotBay Labelled(string label) => new() { Label = label };

    public void Relabel(string label) => Label = label;
}

/// <summary>
/// An aggregate that declined its stamps and its version is created, changed and deleted like any other —
/// the framework's interceptors skip the columns it does not map instead of asking the entry for them.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class UnversionedAggregateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-unversioned-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_table_carries_only_what_the_aggregate_declared()
    {
        await using var database = await StartDatabaseAsync();

        var depot = database.Context.Model.FindEntityType(typeof(Depot))!;

        Assert.Equal("Depots", depot.GetTableName());
        Assert.Equal(["Id", "Name"], depot.GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Save_inserts_it_and_the_store_assigns_the_key()
    {
        await using var database = await StartDatabaseAsync();

        var depot = await SavedAsync("north");

        Assert.True(depot.Id > 0);
        Assert.Equal("north", (await database.Load<Depot>(depot.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task Update_writes_the_change_with_no_version_to_compare()
    {
        await using var database = await StartDatabaseAsync();
        var depot = await SavedAsync("north");

        await Depot.Update(depot.Id, d => d.Rename("south"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("south", (await database.Load<Depot>(depot.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task The_edit_form_loads_a_model_and_saves_it_with_no_version_to_carry()
    {
        await using var database = await StartDatabaseAsync();
        var depot = await SavedAsync("north");
        var model = (await Depot.Model(depot.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        model.Name = "south";
        await Depot.Update(depot.Id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("south", (await database.Load<Depot>(depot.Id, TestContext.Current.CancellationToken))!.Name);
        Assert.Null(typeof(DepotModel).GetProperty("Version"));
    }

    [Theory]
    [InlineData("CreatedAt")]
    [InlineData("UpdatedAt")]
    [InlineData("Version")]
    [InlineData("DeletedAt")]
    public void The_read_face_has_no_column_the_table_does_not(string column)
    {
        var face = typeof(DepotRead);

        var property = face.GetProperty(column);

        Assert.Null(property);
    }

    [Fact]
    public async Task Find_then_Save_writes_the_change_twice_over()
    {
        await using var database = await StartDatabaseAsync();
        var created = await SavedAsync("north");
        var depot = (await Depot.Find(created.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        depot.Rename("south");
        await depot.Save(cancellationToken: TestContext.Current.CancellationToken);
        depot.Rename("east");
        await depot.Save(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("east", (await database.Load<Depot>(created.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task Delete_removes_the_row()
    {
        await using var database = await StartDatabaseAsync();
        var depot = await SavedAsync("north");

        await Depot.Delete(depot.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(await database.Load<Depot>(depot.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, await Depot.Count(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_child_is_added_changed_and_removed_under_a_root_with_nothing_to_touch()
    {
        await using var database = await StartDatabaseAsync();
        var created = Depot.Named("north");
        created.AddBay("A");
        created.AddBay("B");
        await created.Save(cancellationToken: TestContext.Current.CancellationToken);

        await Depot.Update(
            created.Id,
            d =>
            {
                d.Bays.Single(b => b.Label == "A").Relabel("A2");
                d.AddBay("C");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var stored = (await database.Load<Depot>(created.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(["A2", "B", "C"], stored.Bays.Select(b => b.Label).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_write_that_names_a_version_is_refused_because_there_is_none_to_compare()
    {
        await using var database = await StartDatabaseAsync();
        var depot = await SavedAsync("north");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => GeneratedModelWrites.Update<Depot>(
            depot.Id, version: 3, d => d.Rename("south"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Concurrency.None", refused.Message, StringComparison.Ordinal);
        Assert.Equal("north", (await database.Load<Depot>(depot.Id, TestContext.Current.CancellationToken))!.Name);
    }

    private static async Task<Depot> SavedAsync(string name)
    {
        var depot = Depot.Named(name);
        await depot.Save(cancellationToken: TestContext.Current.CancellationToken);
        return depot;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
