using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// Rask.Data's key convention leaves an integer key to the store's identity and marks every other key never
// generated, so the entity's own factory is what assigns it. Saved the way every write is — through a context.
public sealed class Coupon : Aggregate<int>
{
    private Coupon() { }

    public string Code { get; private set; } = "";

    public static Coupon Issue(string code) => new() { Code = code };
}

public readonly record struct ParcelId(Guid Value);

public readonly record struct LockerCode(string Value);

public sealed class Locker : Aggregate<LockerCode>
{
    private Locker() { }

    public string Site { get; private set; } = "";

    public static Locker At(LockerCode code, string site) => new() { Id = code, Site = site };

    public void MoveTo(string site) => Site = site;
}

// Value objects the way RASK084 wants them — no public setters — so EF Core has to materialise them through
// private members; only a round trip shows it can.
public sealed class Parcel : Aggregate<ParcelId>
{
    private Parcel() { }

    public string Label { get; private set; } = "";

    public DeliveryAddress Destination { get; private set; } = null!;

    public ParcelWeight Weight { get; private set; } = null!;

    public static Parcel Send(string label, DeliveryAddress destination, ParcelWeight weight) => new()
    {
        Id = new ParcelId(Guid.CreateVersion7()),
        Label = label,
        Destination = destination,
        Weight = weight,
    };
}

// A private parameterless constructor and private setters.
public sealed class DeliveryAddress
{
    private DeliveryAddress() { }

    public string Street { get; private set; } = "";

    public string City { get; private set; } = "";

    public static DeliveryAddress Of(string street, string city) => new() { Street = street, City = city };
}

// A private constructor naming every property.
public sealed class ParcelWeight
{
    private ParcelWeight(decimal amount, string unit)
    {
        Amount = amount;
        Unit = unit;
    }

    public decimal Amount { get; private set; }

    public string Unit { get; private set; }

    public static ParcelWeight Of(decimal amount, string unit) => new(amount, unit);
}

[Collection(DataDbCollection.Name)]
public sealed class ModelKeyAndValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-keys-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));

    [Fact]
    public async Task An_integer_key_is_generated_by_the_store_on_insert()
    {
        await using var database = await StartDatabaseAsync();

        var first = Coupon.Issue("SPRING");
        var second = Coupon.Issue("SUMMER");
        database.Context.AddRange(first, second);
        await database.Context.SaveChangesAsync();

        Assert.True(first.Id > 0);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("SUMMER", (await database.LoadAsync<Coupon>(second.Id))!.Code);
    }

    [Fact]
    public async Task A_key_the_entity_assigns_is_the_key_inserted()
    {
        await using var database = await StartDatabaseAsync();

        database.Context.Add(Locker.At(new LockerCode("A-12"), "Szeged"));
        await database.Context.SaveChangesAsync();

        Assert.Equal("Szeged", (await database.LoadAsync<Locker>(new LockerCode("A-12")))!.Site);
    }

    [Fact]
    public async Task The_key_convention_leaves_integer_keys_to_the_store_and_every_other_key_to_the_entity()
    {
        await using var database = await StartDatabaseAsync();

        ValueGenerated Of<TEntity>() =>
            database.Context.Model.FindEntityType(typeof(TEntity))!.FindPrimaryKey()!.Properties.Single().ValueGenerated;

        Assert.Equal(ValueGenerated.OnAdd, Of<Coupon>());
        Assert.Equal(ValueGenerated.Never, Of<Parcel>());
        Assert.Equal(ValueGenerated.Never, Of<Locker>());
    }

    [Fact]
    public async Task Value_objects_without_public_setters_round_trip_through_a_save_and_a_read()
    {
        await using var database = await StartDatabaseAsync();

        var parcel = Parcel.Send("fragile", DeliveryAddress.Of("1 Main St", "Szeged"), ParcelWeight.Of(2.5m, "kg"));
        database.Context.Add(parcel);
        await database.Context.SaveChangesAsync();

        var stored = (await database.LoadAsync<Parcel>(parcel.Id))!;
        Assert.Equal("fragile", stored.Label);
        Assert.Equal("1 Main St", stored.Destination.Street);
        Assert.Equal("Szeged", stored.Destination.City);
        Assert.Equal(2.5m, stored.Weight.Amount);
        Assert.Equal("kg", stored.Weight.Unit);
    }

    // ---- the generated writes ------------------------------------------------------------------------
    // The model carries no key, so the key comes from the caller — CreateAsync(id, model), for every entity — or,
    // where CreateAsync(model) exists, from something that can produce one: the store's identity for an integer,
    // the generated create itself for a Guid or a strongly-typed id over one. A key nothing can produce — a
    // strongly-typed id over a string — gets only the id overload.

    private static ParcelModel NewParcel(string label) => new()
    {
        Label = label,
        Destination = new ParcelModel.DeliveryAddressModel { Street = "1 Main St", City = "Szeged" },
        Weight = new ParcelModel.ParcelWeightModel { Amount = 2.5m, Unit = "kg" },
    };

    [Fact]
    public async Task The_generated_create_leaves_an_integer_key_to_the_store()
    {
        await using var database = await StartDatabaseAsync();

        var first = await Coupon.CreateAsync(new CouponModel { Code = "SPRING" });
        var second = await Coupon.CreateAsync(new CouponModel { Code = "SUMMER" });

        Assert.True(first.Id > 0);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("SUMMER", (await database.LoadAsync<Coupon>(second.Id))!.Code);
    }

    [Fact]
    public void A_key_the_store_generates_gets_no_create_that_takes_one()
    {
        // An explicit value in an identity column is refused on SQL Server and leaves PostgreSQL's sequence behind.
        Assert.DoesNotContain(
            typeof(CouponModelExtensions).GetMethods(),
            m => m.Name == "CreateAsync" && m.GetParameters()[0].ParameterType == typeof(int));
    }

    [Fact]
    public async Task A_key_nothing_can_produce_is_inserted_under_the_id_the_caller_gives()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Locker.CreateAsync(new LockerCode("A-12"), new LockerModel { Site = "Szeged" });

        Assert.Equal(new LockerCode("A-12"), created.Id);
        Assert.Equal("Szeged", (await database.LoadAsync<Locker>(new LockerCode("A-12")))!.Site);
        Assert.DoesNotContain(
            typeof(LockerModelExtensions).GetMethods(),
            m => m.Name == "CreateAsync" &&
                 (m.GetParameters()[0].ParameterType == typeof(LockerModel) ||
                  m.GetParameters()[0].ParameterType == typeof(Action<Locker>)));

        // The lambda form takes the key the same way.
        var moved = await Locker.CreateAsync(new LockerCode("B-7"), locker => locker.MoveTo("Debrecen"));
        Assert.Equal(new LockerCode("B-7"), moved.Id);
        Assert.Equal("Debrecen", (await database.LoadAsync<Locker>(new LockerCode("B-7")))!.Site);
    }

    [Fact]
    public async Task A_strongly_typed_guid_key_is_assigned_by_the_generated_create()
    {
        await using var database = await StartDatabaseAsync();

        var first = await Parcel.CreateAsync(NewParcel("first"));
        var second = await Parcel.CreateAsync(NewParcel("second"));

        Assert.NotEqual(Guid.Empty, first.Id.Value);
        Assert.Equal(7, first.Id.Value.Version);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("second", (await database.LoadAsync<Parcel>(second.Id))!.Label);
    }

    [Fact]
    public async Task Value_objects_without_public_setters_round_trip_through_the_generated_create_and_update()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Parcel.CreateAsync(NewParcel("fragile"));

        var stored = (await database.LoadAsync<Parcel>(created.Id))!;
        Assert.Equal("1 Main St", stored.Destination.Street);
        Assert.Equal("Szeged", stored.Destination.City);
        Assert.Equal(2.5m, stored.Weight.Amount);
        Assert.Equal("kg", stored.Weight.Unit);

        var edit = NewParcel("fragile");
        edit.Destination!.City = "Debrecen";
        edit.Weight!.Amount = 3m;
        edit.Weight.Unit = "lb";
        await Parcel.UpdateAsync(created.Id, edit);

        var updated = (await database.LoadAsync<Parcel>(created.Id))!;
        Assert.Equal("1 Main St", updated.Destination.Street);
        Assert.Equal("Debrecen", updated.Destination.City);
        Assert.Equal(3m, updated.Weight.Amount);
        Assert.Equal("lb", updated.Weight.Unit);
    }
}
