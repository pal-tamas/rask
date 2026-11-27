using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A row of the delivery form's parts; <paramref name="Id" /> names a saved one, none a new one.</summary>
public sealed record CargoPartSpec(Guid? Id, string Hold, decimal Tonnes);

// A delivery in parts, its children declared as one HasMany property: what HasManyTests syncs from a form's specs.
public sealed class Cargo : Aggregate<Guid>
{
    private Cargo() { } // EF materialization

    public string Ship { get; private set; } = "";

    public HasMany<CargoPart> Parts { get; } = new();

    public static Cargo Load(string ship, IReadOnlyList<CargoPartSpec> parts)
    {
        var cargo = new Cargo { Id = Guid.CreateVersion7(), Ship = ship };
        cargo.Change(parts);
        return cargo;
    }

    public SyncResult<CargoPart> Change(IReadOnlyList<CargoPartSpec> parts) =>
        Parts.Sync(parts, s => s.Id, CargoPart.For, (p, s) => p.Take(s), p => p.LetGo());

    // Matched by the hold each part sits in, not its Id: the import knows holds, never ids.
    public SyncResult<CargoPart> Restow(IReadOnlyList<CargoPartSpec> parts) =>
        Parts.Sync(parts, s => s.Hold, p => p.Hold, CargoPart.For, (p, s) => p.Take(s));

    public CargoPart Stow(string hold, decimal tonnes)
    {
        var part = CargoPart.For(new CargoPartSpec(null, hold, tonnes));
        Parts.Add(part);
        return part;
    }

    public bool Unload(CargoPart part) => Parts.Remove(part);

    public void StowAll(params CargoPartSpec[] parts) => Parts.Add(parts.Select(CargoPart.For));

    public int UnloadLighterThan(decimal tonnes) => Parts.RemoveAll(p => p.Tonnes < tonnes);

    public void UnloadAll() => Parts.Clear();

    // The ports the cargo calls at: a link, each call naming one port's id and nothing more.
    public HasMany<CargoCall> Calls { get; } = new();

    public SyncResult<CargoCall> CallAt(params Guid[] ports) => Calls.Sync(ports, c => c.PortId, CargoCall.At);

    // The parts load back in the order the last sync gave them.
    public static void Configure(EntityTypeBuilder<Cargo> builder) => builder.InOrder(c => c.Parts);
}

public sealed class CargoPart : Entity<Guid>
{
    private CargoPart() { } // EF materialization

    public string Hold { get; private set; } = "";

    public decimal Tonnes { get; private set; }

    public bool LetGone { get; private set; }

    internal static CargoPart For(CargoPartSpec spec)
    {
        var part = new CargoPart { Id = Guid.CreateVersion7() };
        part.Take(spec);
        return part;
    }

    internal void Take(CargoPartSpec spec) => (Hold, Tonnes) = (spec.Hold, spec.Tonnes);

    internal void LetGo() => LetGone = true;

    private IEnumerable<Problem> Validate()
    {
        if (Tonnes <= 0)
        {
            yield return Problem.At(Tonnes, "A part weighs something.");
        }
    }
}

// A cargo's call at a port: the link child, the port's id and nothing more.
public sealed class CargoCall : Entity<Guid>
{
    private CargoCall() { } // EF materialization

    public Guid PortId { get; private set; }

    internal static CargoCall At(Guid port) => new() { Id = Guid.CreateVersion7(), PortId = port };
}

/// <summary>A row of the barge form's holds; <paramref name="Id" /> names a saved one, none a new one.</summary>
public sealed record BargeHoldSpec(Guid? Id, string Name);

// A barge whose holds are named, each name once per barge: a unique index over the parent and the name.
public sealed class Barge : Aggregate<Guid>
{
    private Barge() { } // EF materialization

    public string Name { get; private set; } = "";

    public HasMany<BargeHold> Holds { get; } = new();

    public static Barge Named(string name, params string[] holds)
    {
        var barge = new Barge { Id = Guid.CreateVersion7(), Name = name };
        barge.Change([.. holds.Select(static h => new BargeHoldSpec(null, h))]);
        return barge;
    }

    public SyncResult<BargeHold> Change(IReadOnlyList<BargeHoldSpec> holds) =>
        Holds.Sync(holds, s => s.Id, BargeHold.For, (h, s) => h.Take(s));
}

public sealed class BargeHold : Entity<Guid>
{
    private BargeHold() { } // EF materialization

    public string Name { get; private set; } = "";

    internal static BargeHold For(BargeHoldSpec spec)
    {
        var hold = new BargeHold { Id = Guid.CreateVersion7() };
        hold.Take(spec);
        return hold;
    }

    internal void Take(BargeHoldSpec spec) => Name = spec.Name;

    public static void Configure(EntityTypeBuilder<BargeHold> builder) => builder.HasIndex("BargeId", nameof(Name)).IsUnique();
}
