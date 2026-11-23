namespace Rask.Data.Tests;

/// <summary>The lengths the value objects below declare by name: a constant is read for the column like a literal.</summary>
public static class RigLimits
{
    public const int Chip = 12;
}

/// <summary>A text with a length, trimmed.</summary>
public sealed partial record RigDriver(string Value) : Text(Value, MaxLength: 20);

/// <summary>A text kept as typed, its length named by a constant.</summary>
public sealed partial record RigChip(string Value) : Text(Value, MaxLength: RigLimits.Chip, Trim: false);

/// <summary>A text with a pattern and its own words for it.</summary>
public sealed partial record RigPlate(string Value) : Text(Value, Pattern: @"^[A-Z]{3}-\d{3}$", Message: RigPlate.Form)
{
    public const string Form = "A plate is three letters and three digits.";
}

/// <summary>A decimal with a least and a scale.</summary>
public sealed partial record RigLitres(decimal Value) : Number(Value, Min: 1, Scale: 2, Precision: 9);

/// <summary>A whole number in a range.</summary>
public sealed partial record RigAxles(long Value) : Whole(Value, Min: 2, Max: 9);

/// <summary>A day that cannot be in the future.</summary>
public sealed partial record RigInspected(DateOnly Value) : Day(Value, NotAfterToday: true);

/// <summary>A strict range: the end after the start.</summary>
public sealed partial record RigKm(int Start, int End) : Range(Start, End, Strict: true);

/// <summary>A range that may be open at its end.</summary>
public sealed partial record RigShift(DateTime From, DateTime? To) : Range(From, To);

/// <summary>A range bounded at both ends.</summary>
public sealed partial record RigMonths(int From, int To) : Range(From, To, Min: 1, Max: 12);

/// <summary>A range of decimals bounded by a whole number, which is converted to the ends' type.</summary>
public sealed partial record RigLoad(decimal Start, decimal End) : Range(Start, End, Min: 1);

/// <summary>A range whose ends are of different types: a programming error.</summary>
public sealed partial record RigMismatch(int Start, long End) : Range(Start, End);

/// <summary>A range bounded by a value its ends cannot be compared with: a programming error.</summary>
public sealed partial record RigWrongBound(DateTime Start, DateTime End) : Range(Start, End, Min: 5);

/// <summary>A value object with a rule of its own.</summary>
public sealed partial record RigOpening(DateOnly? Date, decimal? Quantity) : Value
{
    public const string BothOrNeither = "Both or neither.";

    protected override IEnumerable<Problem> Rules()
    {
        if (Date is null != Quantity is null)
        {
            yield return Problem.At(Quantity, BothOrNeither);
        }
    }
}

/// <summary>A complex value whose member refuses a stored value outright, past any rule: materializing it fails.</summary>
public sealed partial record RigSeal(int Count, string Mark) : Value
{
    public int Count { get; private init; } = Count >= 0 ? Count : throw new InvalidOperationException("A seal count is never negative.");
}

/// <summary>An aggregate holding one value object of each one-value base, with a generated model.</summary>
public sealed class Rig : Aggregate<int, Model, Deletable>
{
    private Rig() { }

    public RigDriver Driver { get; private set; } = RigDriver.Create("Nobody");

    public RigChip? Chip { get; private set; }

    public RigPlate Plate { get; private set; } = RigPlate.Create("AAA-000");

    public RigLitres Tank { get; private set; } = RigLitres.Create(1);

    public RigAxles Axles { get; private set; } = RigAxles.Create(2);

    public RigInspected? Inspected { get; private set; }

    public static Rig Of(string driver, string plate, decimal tank, long axles, string? chip = null, DateOnly? inspected = null) =>
        new()
        {
            Driver = RigDriver.Create(driver, nameof(Driver)),
            Plate = RigPlate.Create(plate, nameof(Plate)),
            Tank = RigLitres.Create(tank, nameof(Tank)),
            Axles = RigAxles.Create(axles, nameof(Axles)),
            Chip = RigChip.CreateOptional(chip, nameof(Chip)),
            Inspected = RigInspected.CreateOptional(inspected, nameof(Inspected)),
        };
}

/// <summary>An aggregate holding value objects of several members, stored as complex values.</summary>
/// <summary>An aggregate written through its generated model, holding a range that checks itself.</summary>
public sealed class Leg : Aggregate<int, Model>
{
    private Leg() { }

    public RigKm Km { get; private set; } = RigKm.Create(0, 1);
}

public sealed class Haul : Aggregate<int>
{
    private Haul() { }

    public RigDriver Driver { get; private set; } = null!;

    public RigKm Km { get; private set; } = null!;

    public RigOpening Opening { get; private set; } = null!;

    public static Haul Of(string driver, int start, int end) =>
        new()
        {
            Driver = RigDriver.Create(driver, nameof(Driver)),
            Km = RigKm.Create(start, end, nameof(Km)),
            Opening = RigOpening.Create(null, null, nameof(Opening)),
        };

    public void Reopen(DateOnly date, decimal quantity) => Opening = RigOpening.Create(date, quantity, nameof(Opening));
}

/// <summary>An aggregate whose complex value cannot be built from a negative count.</summary>
public sealed class Sealed : Aggregate<int>
{
    private Sealed() { }

    public RigSeal Seal { get; private set; } = null!;

    public static Sealed Of(int count) => new() { Seal = RigSeal.Create(count, "A") };
}
