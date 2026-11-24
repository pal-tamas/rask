using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

public readonly partial record struct CaravanId(Guid Value);

// F-18 on an operation: one that writes a second aggregate says why in Configure, and one that must not be raced
// locks its row, or its wagons, before its entry loads it (OperationLockTests).
public sealed partial class Caravan : Aggregate<CaravanId, Versioned>
{
    private readonly List<CaravanWagon> _wagons = [];

    private Caravan() { } // EF materialization

    public string Leader { get; private set; } = "";

    public IReadOnlyCollection<CaravanWagon> Wagons => _wagons;

    public void Lead(string leader) => Leader = leader;

    public void Hitch(string load) => _wagons.Add(CaravanWagon.Of(load));

    public async Task Supply(string widget)
    {
        await Widget.Create(widget).Save();
        Leader = "supplied";
    }

    public async Task Stock(string widget)
    {
        await Widget.Create(widget).Save();
        Leader = "stocked";
    }

    public static void Configure(EntityTypeBuilder<Caravan> b)
    {
        b.Operation(c => c.Supply, manyAggregates: "A caravan sets out with the widgets it is supplied");
        b.Operation(c => c.Lead, forUpdate: true);
        b.Operation(c => c.Hitch, forUpdate: c => c.Wagons);
    }

    internal static Caravan Formed(string leader) => new() { Id = new CaravanId(Guid.CreateVersion7()), Leader = leader };
}

public sealed class CaravanWagon : Entity<Guid>
{
    private CaravanWagon() { }

    public string Load { get; private set; } = "";

    internal static CaravanWagon Of(string load) => new() { Id = Guid.CreateVersion7(), Load = load };
}
