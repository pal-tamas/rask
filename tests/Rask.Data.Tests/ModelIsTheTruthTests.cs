using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A one-value value object held inside another.</summary>
public sealed record HarborPin(string Value);

/// <summary>A value object of several values held inside another: two levels down from the aggregate.</summary>
public sealed record HarborBerth(string Dock, string? Note);

/// <summary>A value object of several values: text and a number that may be empty, and text that may not.</summary>
public sealed class HarborAddress
{
    public const string OutOfRange = "A door is numbered 1 to 99.";

    private HarborAddress() { }

    public string Street { get; private set; } = "";

    public string? Floor { get; private set; }

    [Range(1, 99, ErrorMessage = OutOfRange)]
    public int? Door { get; private set; }

    public HarborPin? Pin { get; private set; }

    public HarborBerth Berth { get; private set; } = new("", null);

    public static HarborAddress Of(string street, string? floor, int? door, string? pin, HarborBerth berth) =>
        new() { Street = street, Floor = floor, Door = door, Pin = pin is null ? null : new HarborPin(pin), Berth = berth };
}

/// <summary>An aggregate holding one value object that must be there and one that may be absent.</summary>
public sealed class Harbor : Aggregate<int>
{
    private Harbor() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public string Name { get; private set; } = "";

    public HarborAddress Address { get; private set; } = HarborAddress.Of("", null, null, null, new("", null));

    public HarborBerth? Spare { get; private set; }

    public static Harbor Full(string name) =>
        new()
        {
            Name = name,
            Address = HarborAddress.Of("1 Quay", "2", 7, "P-1", new HarborBerth("D1", "deep")),
            Spare = new HarborBerth("S9", "shallow"),
        };

    public static void Configure(EntityTypeBuilder<Harbor> builder) => builder.ToTable("Harbors");
}

/// <summary>
/// The model is the truth: a save stores what the nested model of a value object holds. A member it leaves
/// empty is stored empty — never kept from the row — and one that cannot be empty is refused under its path.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ModelIsTheTruthTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-model-truth-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Emptying_one_text_member_stores_NULL_and_leaves_the_others_as_they_were()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Address!.Floor = null;
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("'1 Quay'|NULL|7|'P-1'|'D1'|'deep'|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task Emptying_a_number_stores_NULL()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Address!.Door = null;
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("'1 Quay'|'2'|NULL|'P-1'|'D1'|'deep'|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task Emptying_a_one_value_value_object_inside_stores_NULL()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Address!.Pin = null;
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("'1 Quay'|'2'|7|NULL|'D1'|'deep'|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task Emptying_a_member_two_levels_down_stores_NULL_there_and_nowhere_else()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Address!.Berth!.Note = null;
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|NULL|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task An_update_that_touches_nothing_leaves_the_row_as_it_was_and_empty_text_stays_empty_text()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));
        await database.Context.Database.ExecuteSqlAsync(
            $"UPDATE Harbors SET Address_Floor = '', Address_Berth_Note = '' WHERE Id = {id}", TestContext.Current.CancellationToken);

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(("", ""), (model.Address!.Floor, model.Address.Berth!.Note));
        Assert.Equal("'1 Quay'|''|7|'P-1'|'D1'|''|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task A_member_that_cannot_be_empty_is_refused_under_its_path_and_nothing_is_written()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Address!.Street = null;
        model.Address.Floor = null;
        model.Address.Berth!.Dock = null;
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            [("Address.Berth.Dock", GeneratedModelRules.RequiredMessage), ("Address.Street", GeneratedModelRules.RequiredMessage)],
            refused.Failures.Select(f => (f.Fields.Single(), f.Message)).Order());
        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task An_attribute_on_a_value_objects_own_property_is_checked_by_the_write_under_the_nested_path()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Address!.Door = 500;
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(("Address.Door", HarborAddress.OutOfRange), FailureOf(refused));
        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task A_value_object_that_cannot_be_absent_is_refused_under_its_own_name_when_the_model_holds_none()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var whole = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        whole.Address = null;
        var inner = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        inner.Address!.Berth = null;
        var noAddress = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, whole, cancellationToken: TestContext.Current.CancellationToken));
        var noBerth = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, inner, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(("Address", GeneratedModelRules.RequiredMessage), FailureOf(noAddress));
        Assert.Equal(("Address.Berth", GeneratedModelRules.RequiredMessage), FailureOf(noBerth));
        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task A_value_object_that_may_be_absent_is_cleared_whole_when_the_model_holds_none()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Spare = null;
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|NULL|NULL", await RowAsync(database, id));
        Assert.Null((await database.Load<Harbor>(id, TestContext.Current.CancellationToken))!.Spare);
    }

    [Fact]
    public async Task A_create_stores_what_its_model_holds_and_refuses_the_same_empty_members()
    {
        await using var database = await StartDatabaseAsync();
        var sparse = new HarborModel
        {
            Name = "Baja",
            Address = new HarborModel.HarborAddressModel { Street = "3 Pier", Berth = new HarborModel.HarborBerthModel { Dock = "D4" } },
        };
        var broken = new HarborModel { Name = "Mohács", Address = new HarborModel.HarborAddressModel { Floor = "1" } };

        var created = await Harbor.Create(sparse, cancellationToken: TestContext.Current.CancellationToken);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Create(broken, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("'3 Pier'|NULL|NULL|NULL|'D4'|NULL|NULL|NULL", await RowAsync(database, created.Id));
        Assert.Equal(
            ["Address.Berth", "Address.Street"],
            refused.Failures.Select(f => f.Fields.Single()).Order());
    }

    private static (string Field, string Message) FailureOf(RaskValidationException refused)
    {
        var failure = Assert.Single(refused.Failures);
        return (failure.Fields.Single(), failure.Message);
    }

    // Every value-object column as SQLite spells it, so NULL and '' cannot be mistaken for each other.
    private static async Task<string> RowAsync(TestDatabase database, int id) =>
        (await database.Context.Database
            .SqlQuery<string>($"""
                SELECT quote(Address_Street) || '|' || quote(Address_Floor) || '|' || quote(Address_Door) || '|' || quote(Address_Pin)
                    || '|' || quote(Address_Berth_Dock) || '|' || quote(Address_Berth_Note)
                    || '|' || quote(Spare_Dock) || '|' || quote(Spare_Note) AS Value
                FROM Harbors WHERE Id = {id}
                """)
            .ToListAsync(TestContext.Current.CancellationToken)).Single();

    private static async Task<int> SaveAsync(Harbor harbor)
    {
        await harbor.Save(cancellationToken: TestContext.Current.CancellationToken);
        return harbor.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
