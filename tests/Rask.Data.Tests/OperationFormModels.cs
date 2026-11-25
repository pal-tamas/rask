namespace Rask.Data.Tests;

/// <summary>What a truck of a convoy carries: at least one tonne.</summary>
public sealed partial record ConvoyLoad(decimal Value) : Number(Value, Min: 1);

/// <summary>What a convoy pays at a toll gate: a value object of one value, declared from <c>Value</c>.</summary>
public sealed partial record ConvoyToll(decimal Value) : Value;

/// <summary>When a convoy leaves, and for how many days: a day it must have, a length it may.</summary>
public sealed partial record ConvoyWindow(DateOnly From, int? Days) : Value
{
    protected override IEnumerable<Problem> Rules()
    {
        if (Days <= 0)
        {
            yield return Problem.At(Days, "A window lasts at least a day.");
        }
    }
}

/// <summary>A row of the convoy form's trucks; <paramref name="Id" /> names a stored one, none a new one.</summary>
public sealed record ConvoyTruckSpec(Guid? Id, string Plate, ConvoyLoad Load);

// A partial aggregate whose operations take a list of rows for its Children<,> and value objects of several values: what
// the generated Form.For of each holds (OperationFormEntryTests).
public sealed partial class Convoy : Aggregate<Guid, Versioned>
{
    internal const string Banned = "BANNED";

    private Convoy() { } // EF materialization

    public string Name { get; private set; } = "";

    public Children<ConvoyTruck, ConvoyTruckSpec> Trucks { get; } = new();

    public RigOpening Opening { get; private set; } = null!;

    public ConvoyWindow? Window { get; private set; }

    public RigKm Km { get; private set; } = null!;

    public void Line(IReadOnlyList<ConvoyTruckSpec> trucks)
    {
        for (var i = 0; i < trucks.Count; i++)
        {
            Refuse.When(string.Equals(trucks[i].Plate, Banned, StringComparison.Ordinal), () => trucks[i].Plate, "That truck is banned.");
        }

        _ = Trucks.Sync(trucks, s => s.Id, ConvoyTruck.For, (t, s) => t.Take(s));
    }

    public void Open(RigOpening opening) => Opening = opening;

    public void Plan(ConvoyWindow? window) => Window = window;

    public void Measure(RigKm km) => Km = km;

    public ConvoyToll? Toll { get; private set; }

    public decimal? Fee { get; private set; }

    // A plain parameter over a value object of one value, and a value object over a plain value: each read off the other.
    public void Charge(decimal? toll, ConvoyToll? fee) => (Toll, Fee) = (toll is { } paid ? ConvoyToll.Create(paid) : null, fee?.Value);

    public int RestDays { get; private set; }

    // Nothing on the convoy is called Days, so its form starts from the declared default.
    public void Rest(int days = 2, DateOnly? from = null) => RestDays = from is null ? days : days + 1;

    // The tests' own way to a stored convoy.
    internal static Convoy Assembled(string name, params ConvoyTruckSpec[] trucks)
    {
        var convoy = new Convoy { Id = Guid.CreateVersion7(), Name = name, Km = RigKm.Create(0, 10), Opening = RigOpening.Create(null, null) };
        _ = convoy.Trucks.Sync(trucks, s => s.Id, ConvoyTruck.For, (t, s) => t.Take(s));
        return convoy;
    }
}

public sealed class ConvoyTruck : Entity<Guid>
{
    private ConvoyTruck() { } // EF materialization

    public string Plate { get; private set; } = "";

    public decimal Load { get; private set; }

    internal static ConvoyTruck For(ConvoyTruckSpec spec)
    {
        var truck = new ConvoyTruck { Id = Guid.CreateVersion7() };
        truck.Take(spec);
        return truck;
    }

    internal void Take(ConvoyTruckSpec spec) => (Plate, Load) = (spec.Plate, spec.Load.Value);

    private IEnumerable<Problem> Validate()
    {
        if (Plate.Length > 8)
        {
            yield return Problem.At(Plate, "A plate is at most 8 characters.");
        }
    }
}
