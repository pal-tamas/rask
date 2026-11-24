namespace Rask.Data.Tests;

/// <summary>A row of the delivery form's parts; <paramref name="Id" /> names a saved one, none a new one.</summary>
public sealed record CargoPartSpec(Guid? Id, string Hold, decimal Tonnes);

// A delivery in parts, its children declared as one Children<,> property: what ChildrenSyncTests syncs from a form's specs.
public sealed class Cargo : Aggregate<Guid>
{
    private Cargo() { } // EF materialization

    public string Ship { get; private set; } = "";

    public Children<CargoPart, CargoPartSpec> Parts { get; } = new();

    public static Cargo Load(string ship, IReadOnlyList<CargoPartSpec> parts)
    {
        var cargo = new Cargo { Id = Guid.CreateVersion7(), Ship = ship };
        cargo.Change(parts);
        return cargo;
    }

    public SyncResult<CargoPart> Change(IReadOnlyList<CargoPartSpec> parts) =>
        Parts.Sync(parts, s => s.Id, CargoPart.For, (p, s) => p.Take(s), p => p.LetGo());
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
