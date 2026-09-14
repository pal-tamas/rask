using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// Rask.Data's key convention leaves an integer key to the store's identity and marks every other key never
// generated, so the entity's own factory is what assigns it. Saved the way every write is — through a context.
public sealed class Coupon : Model<int>
{
    private Coupon() { }

    public string Code { get; private set; } = "";

    public static Coupon Issue(string code) => new() { Code = code };
}

public readonly record struct ParcelId(Guid Value);

public readonly record struct LockerCode(string Value);

public sealed class Locker : Model<LockerCode>
{
    private Locker() { }

    public string Site { get; private set; } = "";

    public static Locker At(LockerCode code, string site) => new() { Id = code, Site = site };
}

// Value objects the way RASK084 wants them — no public setters — so EF Core has to materialise them through
// private members; only a round trip shows it can.
public sealed class Parcel : Model<ParcelId>
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
public sealed class DeliveryAddress : IValueObject
{
    private DeliveryAddress() { }

    public string Street { get; private set; } = "";

    public string City { get; private set; } = "";

    public static DeliveryAddress Of(string street, string city) => new() { Street = street, City = city };
}

// A private constructor naming every property.
public sealed class ParcelWeight : IValueObject
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
        Assert.Equal("SUMMER", (await Coupon.FindAsync(second.Id))!.Code);
    }

    [Fact]
    public async Task A_key_the_entity_assigns_is_the_key_inserted()
    {
        await using var database = await StartDatabaseAsync();

        database.Context.Add(Locker.At(new LockerCode("A-12"), "Szeged"));
        await database.Context.SaveChangesAsync();

        Assert.Equal("Szeged", (await Locker.FindAsync(new LockerCode("A-12")))!.Site);
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

        var stored = (await Parcel.FindAsync(parcel.Id))!;
        Assert.Equal("fragile", stored.Label);
        Assert.Equal("1 Main St", stored.Destination.Street);
        Assert.Equal("Szeged", stored.Destination.City);
        Assert.Equal(2.5m, stored.Weight.Amount);
        Assert.Equal("kg", stored.Weight.Unit);
    }
}
