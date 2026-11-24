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

/// <summary>A text that may be left empty, at most 5 characters when filled.</summary>
public sealed partial record RigNote(string Value) : Text(Value, MaxLength: 5, Required: false);

/// <summary>A text kept in capitals, of exactly 17 characters.</summary>
public sealed partial record RigVin(string Value) : Text(Value, MinLength: 17, MaxLength: 17, Upper: true);

/// <summary>A text kept in capitals that the pattern checks as kept.</summary>
public sealed partial record RigCode(string Value) : Text(Value, Pattern: "^[A-ZÁÉÍÓÖŐÚÜŰ]+$", Upper: true);

/// <summary>A text with a pattern and its own words for it.</summary>
public sealed partial record RigPlate(string Value) : Text(Value, Pattern: @"^[A-Z]{3}-\d{3}$", Message: RigPlate.Form)
{
    public const string Form = "A plate is three letters and three digits.";
}

/// <summary>A decimal with a least and a scale.</summary>
public sealed partial record RigLitres(decimal Value) : Number(Value, Min: 1, Scale: 2, Precision: 9);

/// <summary>A computed price, rounded to its scale instead of refused for its extra digits.</summary>
public sealed partial record RigUnitPrice(decimal Value) : Number(Value, Min: 0, Scale: 2, Precision: 6, Round: true);

/// <summary>A number asked to round with no scale to round to: a programming error.</summary>
public sealed partial record RigUnscaled(decimal Value) : Number(Value, Round: true);

/// <summary>The words every rule of each base is told in, when it states its own.</summary>
public static class RigWords
{
    public const string Serial = "A serial is three to five capital letters.";
    public const string Count = "Count at least one.";
    public const string Share = "A share is a whole percent, at most 100.";
    public const string Due = "The due day cannot be in the future.";
    public const string Read = "The reading cannot be in the future.";
    public const string Odometer = "A záró km-nek nagyobbnak kell lennie a kezdőnél.";
}

/// <summary>A text with every rule, said in its own words.</summary>
public sealed partial record RigSerial(string Value) : Text(Value, MinLength: 3, MaxLength: 5, Pattern: "^[A-Z]+$", Message: RigWords.Serial);

/// <summary>A whole number said in its own words.</summary>
public sealed partial record RigCount(long Value) : Whole(Value, Min: 1, Message: RigWords.Count);

/// <summary>A number said in its own words.</summary>
public sealed partial record RigShare(decimal Value) : Number(Value, Max: 100, Scale: 0, Message: RigWords.Share);

/// <summary>A day said in its own words.</summary>
public sealed partial record RigDue(DateOnly Value) : Day(Value, NotAfterToday: true, Message: RigWords.Due);

/// <summary>A moment said in its own words.</summary>
public sealed partial record RigMeterRead(DateTime Value) : Moment(Value, NotAfterNow: true, Message: RigWords.Read);

/// <summary>A range said in its own words.</summary>
public sealed partial record RigOdometer(int StartKm, int EndKm) : Range(StartKm, EndKm, Strict: true, Min: 0, Message: RigWords.Odometer);

/// <summary>A whole number in a range.</summary>
public sealed partial record RigAxles(long Value) : Whole(Value, Min: 2, Max: 9);

/// <summary>A day that cannot be in the future.</summary>
public sealed partial record RigInspected(DateOnly Value) : Day(Value, NotAfterToday: true);

/// <summary>A moment on the wall clock that cannot be in the future.</summary>
public sealed partial record RigFueled(DateTime Value) : Moment(Value, NotAfterNow: true);

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

    public RigFueled? Fueled { get; private set; }

    public static Rig Of(
        string driver, string plate, decimal tank, long axles, string? chip = null, DateOnly? inspected = null, DateTime? fueled = null) =>
        new()
        {
            Driver = RigDriver.Create(driver, nameof(Driver)),
            Plate = RigPlate.Create(plate, nameof(Plate)),
            Tank = RigLitres.Create(tank, nameof(Tank)),
            Axles = RigAxles.Create(axles, nameof(Axles)),
            Chip = RigChip.CreateOptional(chip, nameof(Chip)),
            Inspected = RigInspected.CreateOptional(inspected, nameof(Inspected)),
            Fueled = RigFueled.CreateOptional(fueled, nameof(Fueled)),
        };
}

/// <summary>An aggregate holding value objects of several members, stored as complex values.</summary>
/// <summary>A value of one member with a rule of its own: a one-value value object, like a base record's.</summary>
public sealed partial record RigRank(int Value) : Value
{
    public const string AtLeastOne = "A rank is 1 or more.";

    protected override IEnumerable<Problem> Rules()
    {
        if (Value < 1)
        {
            yield return Problem.Whole(AtLeastOne);
        }
    }
}

/// <summary>A whole number held as an int.</summary>
public sealed partial record RigSeats(int Value) : Whole(Value, Min: 1, Max: 9);

/// <summary>An aggregate holding one-value value objects of an int, written through its generated model.</summary>
public sealed class Ladder : Aggregate<int, Model>
{
    private Ladder() { }

    public RigRank Rank { get; private set; } = null!;

    public RigSeats Seats { get; private set; } = null!;
}

/// <summary>An aggregate holding a text that may be left empty, written through its generated model.</summary>
public sealed class Notebook : Aggregate<int, Model>
{
    private Notebook() { }

    public RigNote Note { get; private set; } = RigNote.Create("");
}

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

/// <summary>A quantity stored to sixteen decimal places, as a tank's opening stock is.</summary>
public sealed partial record RigQuantity(decimal Value) : Number(Value, Precision: 32, Scale: 16);

/// <summary>A complex value of required members, one a one-value value object of its own: a tank's opening stock.</summary>
public sealed partial record RigStart(DateTime Date, RigQuantity Quantity) : Value;

/// <summary>An aggregate whose complex values are optional: a row may have none.</summary>
public sealed class Tankyard : Aggregate<int, Model>
{
    private Tankyard() { }

    public string Name { get; private set; } = "";

    public RigStart? Start { get; private set; }

    public static Tankyard Of(string name) => new() { Name = name };

    public void Open(DateTime date, decimal quantity)
    {
        Start = RigStart.Create(date, RigQuantity.Create(quantity), nameof(Start));
    }
}

/// <summary>A text declared straight from Value, with no base of its own: a chip as the reader hands it over.</summary>
public sealed partial record RigTag(string Value) : Value;

/// <summary>A decimal declared straight from Value.</summary>
public sealed partial record RigReading(decimal Value) : Value;

/// <summary>A value declared straight from Value that says how it prints.</summary>
public sealed partial record RigSign(int Value) : Value
{
    public override string ToString() => "#" + Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
