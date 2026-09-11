using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// The generated model carries no key, so a key comes from the caller — CreateAsync(id, model), for every entity —
// or, where CreateAsync(model) exists, from something that can produce one: the store's identity for an integer,
// the generated create itself for a Guid or a strongly-typed id over one. Rask.Data's key convention marks every
// other key never generated, so EF produces nothing else, and a key nothing can produce — a strongly-typed id over a
// string, below — gets only the id overload.
public sealed class Coupon : Model<int>
{
    private Coupon() { }

    public string Code { get; private set; } = "";
}

public readonly record struct ParcelId(Guid Value);

public readonly record struct LockerCode(string Value);

public sealed class Locker : Model<LockerCode>
{
    private Locker() { }

    public string Site { get; private set; } = "";
}

// Value objects the way RASK084 wants them — no public setters. The generated writes reach them through
// [UnsafeAccessor], which compiles whatever the member names are; only running it shows the names are right.
public sealed class Parcel : Model<ParcelId>
{
    private Parcel() { }

    public string Label { get; private set; } = "";

    public DeliveryAddress Destination { get; private set; } = null!;

    public ParcelWeight Weight { get; private set; } = null!;
}

// A private parameterless constructor and private setters: rebuilt member by member.
public sealed class DeliveryAddress : IValueObject
{
    private DeliveryAddress() { }

    public string Street { get; private set; } = "";

    public string City { get; private set; } = "";
}

// A private constructor naming every property: rebuilt through it.
public sealed class ParcelWeight : IValueObject
{
    private ParcelWeight(decimal amount, string unit)
    {
        Amount = amount;
        Unit = unit;
    }

    public decimal Amount { get; private set; }

    public string Unit { get; private set; }
}

[Collection(DataDbCollection.Name)]
public sealed class GeneratedModelKeyAndValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-keys-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));

    private static ParcelModel NewParcel(string label) => new()
    {
        Label = label,
        Destination = new ParcelModel.DeliveryAddressModel { Street = "1 Main St", City = "Szeged" },
        Weight = new ParcelModel.ParcelWeightModel { Amount = 2.5m, Unit = "kg" },
    };

    [Fact]
    public async Task An_integer_key_is_generated_by_the_store_on_insert()
    {
        await using var database = await StartDatabaseAsync();

        var first = await Coupon.CreateAsync(new CouponModel { Code = "SPRING" });
        var second = await Coupon.CreateAsync(new CouponModel { Code = "SUMMER" });

        Assert.True(first.Id > 0);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("SUMMER", (await Coupon.FindAsync(second.Id))!.Code);
    }

    [Fact]
    public async Task An_integer_key_given_to_CreateAsync_is_the_key_inserted()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Coupon.CreateAsync(4200, new CouponModel { Code = "AUTUMN" });

        Assert.Equal(4200, created.Id);
        Assert.Equal("AUTUMN", (await Coupon.FindAsync(4200))!.Code);

        // And the identity carries on past it.
        Assert.True((await Coupon.CreateAsync(new CouponModel { Code = "WINTER" })).Id > 4200);
    }

    [Fact]
    public async Task A_key_nothing_can_produce_is_inserted_under_the_id_the_caller_gives()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Locker.CreateAsync(new LockerCode("A-12"), new LockerModel { Site = "Szeged" });

        Assert.Equal(new LockerCode("A-12"), created.Id);
        Assert.Equal("Szeged", (await Locker.FindAsync(new LockerCode("A-12")))!.Site);
    }

    [Fact]
    public async Task The_key_convention_leaves_integer_keys_to_the_store_and_every_other_key_to_the_entity()
    {
        await using var database = await StartDatabaseAsync();

        ValueGenerated Of<TEntity>() =>
            database.Context.Model.FindEntityType(typeof(TEntity))!.FindPrimaryKey()!.Properties.Single().ValueGenerated;

        Assert.Equal(ValueGenerated.OnAdd, Of<Coupon>());
        Assert.Equal(ValueGenerated.Never, Of<Invoice>());
        Assert.Equal(ValueGenerated.Never, Of<Parcel>());
        Assert.Equal(ValueGenerated.Never, Of<Locker>());
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
        Assert.Equal("second", (await Parcel.FindAsync(second.Id))!.Label);
    }

    [Fact]
    public async Task Value_objects_without_public_setters_round_trip_through_create_ToModel_and_update()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Parcel.CreateAsync(NewParcel("fragile"));

        var stored = (await Parcel.FindAsync(created.Id))!;
        Assert.Equal("1 Main St", stored.Destination.Street);
        Assert.Equal("Szeged", stored.Destination.City);
        Assert.Equal(2.5m, stored.Weight.Amount);
        Assert.Equal("kg", stored.Weight.Unit);

        var edit = stored.ToModel();
        Assert.Equal("Szeged", edit.Destination.City);
        Assert.Equal("kg", edit.Weight.Unit);

        edit.Destination.City = "Debrecen";
        edit.Weight.Amount = 3m;
        edit.Weight.Unit = "lb";
        await Parcel.UpdateAsync(created.Id, edit);

        var updated = (await Parcel.FindAsync(created.Id))!;
        Assert.Equal("1 Main St", updated.Destination.Street);
        Assert.Equal("Debrecen", updated.Destination.City);
        Assert.Equal(3m, updated.Weight.Amount);
        Assert.Equal("lb", updated.Weight.Unit);
    }
}
