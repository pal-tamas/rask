using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Core.Forms;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A one-value value object held inside another.</summary>
public sealed record HarborPin(string Value);

/// <summary>A value object of several values held inside another: two levels down from the aggregate.</summary>
public sealed record HarborBerth(string Dock, string? Note);

/// <summary>A value object of several values that is only ever held where it must be there.</summary>
public sealed record HarborGate(string Code, int Lane);

/// <summary>
/// A value object of several values, written as a plain class on <see cref="ValueObject" />: text and a number that
/// may be empty, and text that may not.
/// </summary>
public sealed class HarborAddress : ValueObject
{
    public const string OutOfRange = "A door is numbered 1 to 99.";

    private HarborAddress() { }

    public string Street { get; private set; } = "";

    public string? Floor { get; private set; }

    [Range(1, 99, ErrorMessage = OutOfRange)]
    public int? Door { get; private set; }

    public HarborPin? Pin { get; private set; }

    public bool? Lit { get; private set; }

    public HarborBerth Berth { get; private set; } = new("", null);

    public static HarborAddress Of(string street, string? floor, int? door, string? pin, HarborBerth berth) =>
        new() { Street = street, Floor = floor, Door = door, Pin = pin is null ? null : new HarborPin(pin), Berth = berth };

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Street;
        yield return Floor;
        yield return Door;
        yield return Pin;
        yield return Lit;
        yield return Berth;
    }
}

/// <summary>An aggregate holding one value object that must be there and one that may be absent.</summary>
public sealed class Harbor : Aggregate<int, Model, Deletable>
{
    private Harbor() { }

    public string Name { get; private set; } = "";

    public HarborAddress Address { get; private set; } = HarborAddress.Of("", null, null, null, new("", null));

    public HarborBerth? Spare { get; private set; }

    public HarborAddress? Annex { get; private set; }

    public HarborGate Gate { get; private set; } = new("G", 1);

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

    // ---- one the aggregate may be without, left wholly empty, was not given ----

    [Fact]
    public async Task An_optional_value_object_whose_model_is_left_wholly_empty_is_stored_as_nothing_and_asked_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var model = new HarborModel
        {
            Name = "Baja",
            Address = new HarborModel.HarborAddressModel { Street = "3 Pier", Berth = new HarborModel.HarborBerthModel { Dock = "D4" } },
            Spare = new HarborModel.HarborBerthModel { Dock = "", Note = "   " },
            Annex = new HarborModel.HarborAddressModel { Berth = new HarborModel.HarborBerthModel() },
        };

        var created = await Harbor.Create(model, cancellationToken: TestContext.Current.CancellationToken);
        var stored = (await database.Load<Harbor>(created.Id, TestContext.Current.CancellationToken))!;

        Assert.Equal("'3 Pier'|NULL|NULL|NULL|'D4'|NULL|NULL|NULL", await RowAsync(database, created.Id));
        Assert.Equal("NULL|NULL|NULL|NULL|NULL|NULL", await AnnexAsync(database, created.Id));
        Assert.Null(stored.Spare);
        Assert.Null(stored.Annex);
    }

    [Fact]
    public async Task An_optional_value_object_that_was_stored_is_cleared_when_an_update_empties_every_field_of_it()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Spare!.Dock = null;
        model.Spare.Note = null;
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|NULL|NULL", await RowAsync(database, id));
        Assert.Null((await database.Load<Harbor>(id, TestContext.Current.CancellationToken))!.Spare);
    }

    [Fact]
    public async Task An_optional_value_object_with_one_field_filled_is_given_and_refused_for_what_it_still_lacks()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Spare = new HarborModel.HarborBerthModel { Note = "tidal" };
        model.Annex = new HarborModel.HarborAddressModel { Berth = new HarborModel.HarborBerthModel { Note = "far side" } };
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            ["Annex.Berth.Dock", "Annex.Street", "Spare.Dock"],
            refused.Failures.Select(f => f.Fields.Single()).Order());
        Assert.All(refused.Failures, failure => Assert.Equal(GeneratedModelRules.RequiredMessage, failure.Message));
        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|'S9'|'shallow'", await RowAsync(database, id));
    }

    [Fact]
    public async Task An_unticked_box_says_nothing_and_a_ticked_one_makes_an_optional_value_object_given()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var unticked = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        unticked.Annex = new HarborModel.HarborAddressModel { Lit = false, Berth = new HarborModel.HarborBerthModel() };
        var ticked = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        ticked.Annex = new HarborModel.HarborAddressModel { Lit = true };
        await Harbor.Update(id, unticked, cancellationToken: TestContext.Current.CancellationToken);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, ticked, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("NULL|NULL|NULL|NULL|NULL|NULL", await AnnexAsync(database, id));
        Assert.Contains(refused.Failures, failure => failure.Fields.Single() == "Annex.Street");
    }

    [Fact]
    public async Task An_empty_model_over_a_stored_value_object_that_holds_nothing_either_leaves_the_row_exactly_as_it_is()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));
        await database.Context.Database.ExecuteSqlAsync(
            $"UPDATE Harbors SET Spare_Dock = '', Spare_Note = '' WHERE Id = {id}", TestContext.Current.CancellationToken);

        var untouched = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        await Harbor.Update(id, untouched, cancellationToken: TestContext.Current.CancellationToken);
        var afterUntouched = await RowAsync(database, id);
        var absent = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        absent.Spare = null;
        await Harbor.Update(id, absent, cancellationToken: TestContext.Current.CancellationToken);

        // Text stored as '' stays '': nothing stored was a value, so there is nothing to clear.
        Assert.Equal(("", ""), (untouched.Spare!.Dock, untouched.Spare.Note));
        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|''|''", afterUntouched);
        Assert.Equal("'1 Quay'|'2'|7|'P-1'|'D1'|'deep'|''|''", await RowAsync(database, id));
    }

    [Fact]
    public async Task A_number_or_a_one_value_value_object_alone_is_enough_for_an_optional_value_object_to_count_as_given()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var byNumber = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        byNumber.Annex = new HarborModel.HarborAddressModel { Door = 0 };
        var byPin = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        byPin.Annex = new HarborModel.HarborAddressModel { Pin = "P-9" };
        var zero = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, byNumber, cancellationToken: TestContext.Current.CancellationToken));
        var pin = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, byPin, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(zero.Failures, failure => failure.Fields.Single() == "Annex.Street");
        Assert.Contains(pin.Failures, failure => failure.Fields.Single() == "Annex.Street");
    }

    [Fact]
    public async Task An_optional_value_object_holding_another_is_stored_whole_two_levels_down()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Annex = new HarborModel.HarborAddressModel { Street = "9 Mole", Berth = new HarborModel.HarborBerthModel { Dock = "A2" } };
        await Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);
        var filled = await AnnexAsync(database, id);
        var again = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        again.Annex!.Street = " ";
        again.Annex.Berth!.Dock = null;
        await Harbor.Update(id, again, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("'9 Mole'|NULL|NULL|NULL|'A2'|NULL", filled);
        Assert.Equal("NULL|NULL|NULL|NULL|NULL|NULL", await AnnexAsync(database, id));
    }

    [Fact]
    public async Task A_value_object_that_cannot_be_absent_is_still_refused_when_its_model_is_left_wholly_empty()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Harbor.Full("Szeged"));

        var model = (await Harbor.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        model.Gate = new HarborModel.HarborGateModel();
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Harbor.Update(id, model, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["Gate.Code", "Gate.Lane"], refused.Failures.Select(f => f.Fields.Single()).Order());
    }

    [Fact]
    public void A_form_announces_a_nested_field_as_required_where_it_must_be_there_and_not_where_it_may_be_absent()
    {
        var model = new HarborModel();

        var root = RaskValidation.IsRequired(() => model.Name);
        var mustBeThere = (RaskValidation.IsRequired(() => model.Gate!.Code), RaskValidation.IsRequired(() => model.Address!.Street));
        var mustBeThereTwoDown = RaskValidation.IsRequired(() => model.Address!.Berth!.Dock);
        var mayBeAbsent = (RaskValidation.IsRequired(() => model.Spare!.Dock), RaskValidation.IsRequired(() => model.Annex!.Street));
        var mayBeAbsentTwoDown = RaskValidation.IsRequired(() => model.Annex!.Berth!.Dock);

        // The same two value-object types are held both ways — HarborAddress as Address and Annex, HarborBerth
        // as Address.Berth and Spare — and each slot answers for itself.
        Assert.True(root);
        Assert.Equal((true, true), mustBeThere);
        Assert.True(mustBeThereTwoDown);
        Assert.Equal((false, false), mayBeAbsent);
        Assert.False(mayBeAbsentTwoDown);
    }

    private static async Task<string> AnnexAsync(TestDatabase database, int id) =>
        (await database.Context.Database
            .SqlQuery<string>($"""
                SELECT quote(Annex_Street) || '|' || quote(Annex_Floor) || '|' || quote(Annex_Door) || '|' || quote(Annex_Pin)
                    || '|' || quote(Annex_Berth_Dock) || '|' || quote(Annex_Berth_Note) AS Value
                FROM Harbors WHERE Id = {id}
                """)
            .ToListAsync(TestContext.Current.CancellationToken)).Single();

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
