using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests.Arrays;

/// <summary>A note of numbered tenants whose rows are an auto-property set to an empty collection expression.</summary>
public sealed class Tanker : Aggregate<int, PerTenant<int>>
{
    public string Number { get; private set; } = "";

    public IReadOnlyList<TankerFill> Fuellings { get; private set; } = [];

    public static Tanker Issue(string number, params (int PumpNo, decimal Quantity, int[] Readings)[] rows)
    {
        var tanker = new Tanker { Number = number };
        tanker.Fill(rows);
        return tanker;
    }

    public void Fill(IEnumerable<(int PumpNo, decimal Quantity, int[] Readings)> rows) =>
        Fuellings = [.. rows.Select(static r => TankerFill.For(r.PumpNo, r.Quantity, r.Readings))];
}

/// <summary>A row keyed by its note and a pump, holding grandchildren declared as an empty array.</summary>
public sealed partial class TankerFill : Entity<(Tanker, int PumpNo)>
{
    public int PumpNo { get; private set; }

    public decimal Quantity { get; private set; }

    public IReadOnlyCollection<FillReading> Readings { get; private set; } = Array.Empty<FillReading>();

    internal static TankerFill For(int pumpNo, decimal quantity, int[] readings) => new()
    {
        PumpNo = pumpNo,
        Quantity = quantity,
        Readings = [.. readings.Select(FillReading.For)],
    };
}

/// <summary>A grandchild keyed under the fuelling that holds it.</summary>
public sealed partial class FillReading : Entity<(TankerFill, int Litres)>
{
    public int Litres { get; private set; }

    internal static FillReading For(int litres) => new() { Litres = litres };
}

/// <summary>A tote of <see cref="Guid" /> tenants whose items have keys the store hands out.</summary>
public sealed class Tote : Aggregate<Guid, PerTenant>
{
    public string Label { get; private set; } = "";

    public IEnumerable<ToteItem> Items { get; private set; } = [];

    public static Tote Packed(string label, params string[] items) =>
        new() { Id = Guid.CreateVersion7(), Label = label, Items = [.. items.Select(ToteItem.Named)] };

    public void Add(string item) => Items = [.. Items, ToteItem.Named(item)];

    public void Remove(string item) => Items = [.. Items.Where(i => i.Name != item)];
}

public sealed class ToteItem : Entity<int>
{
    public string Name { get; private set; } = "";

    public IReadOnlyList<ToteItemTag> Tags { get; private set; } = new ToteItemTag[0];

    public void Tag(string tag) => Tags = [.. Tags, ToteItemTag.Named(tag)];

    internal static ToteItem Named(string name) => new() { Name = name, Tags = [ToteItemTag.Named("new")] };
}

public sealed class ToteItemTag : Entity<int>
{
    public string Text { get; private set; } = "";

    internal static ToteItemTag Named(string text) => new() { Text = text };
}

/// <summary>A squad with a form: its posts and its skills are auto-properties set to an empty collection expression.</summary>
public sealed class Squad : Aggregate<Guid, Model>
{
    public string Name { get; private set; } = "";

    public IReadOnlyList<string> Skills { get; private set; } = [];

    public IReadOnlyList<SquadPost> Posts { get; private set; } = [];
}

public sealed class SquadPost : Entity<Guid>
{
    public string Shift { get; private set; } = "";
}

/// <summary>A context of the app's own, wired by hand: it maps the tankers and asks for Rask's conventions.</summary>
public sealed class HandWiredTankerContext(DbContextOptions<HandWiredTankerContext> options) : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tanker>();
        modelBuilder.ApplyRaskConventions(this);
    }
}
