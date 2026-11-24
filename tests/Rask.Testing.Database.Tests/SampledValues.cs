namespace Rask.Testing.Database.Tests;

/// <summary>A vehicle number: exactly 17 characters, kept in capitals.</summary>
public sealed partial record Vin(string Value) : Text(Value, MinLength: 17, MaxLength: 17, Upper: true);

/// <summary>A name with only a greatest length, shorter than the usual sample.</summary>
public sealed partial record Initials(string Value) : Text(Value, MaxLength: 3);

/// <summary>A Hungarian tax number: a pattern a sample can meet.</summary>
public sealed partial record TaxNumber(string Value) : Text(Value, Pattern: @"^\d{8}-\d-\d{2}$");

/// <summary>A plate: a class, a counted repeat, a group with alternatives.</summary>
public sealed partial record Plate(string Value) : Text(Value, Pattern: @"^(?:[A-Z]{3}|[A-Z]{4})-\d{3}$");

/// <summary>A pattern no simple sample meets: a lookahead.</summary>
public sealed partial record Password(string Value) : Text(Value, Pattern: @"^(?=.*\d).{8,}$");

/// <summary>An email address longer than the usual sample.</summary>
public sealed partial record MailAddress(string Value) : Email(Value, MinLength: 24);

/// <summary>A phone number.</summary>
public sealed partial record Mobile(string Value) : Phone(Value);

/// <summary>A web address.</summary>
public sealed partial record Homepage(string Value) : Url(Value);

/// <summary>A decimal with a least below one step of its scale.</summary>
public sealed partial record Litres(decimal Value) : Number(Value, Min: 0.005m, Scale: 2);

/// <summary>A decimal with no rule beyond being a number.</summary>
public sealed partial record Price(decimal Value) : Number(Value);

/// <summary>A whole number in a band.</summary>
public sealed partial record Axles(long Value) : Whole(Value, Min: 2, Max: 9);

/// <summary>A whole number that may only be below zero.</summary>
public sealed partial record Debt(long Value) : Whole(Value, Max: -1);

/// <summary>A day that cannot be in the future.</summary>
public sealed partial record Inspected(DateOnly Value) : Day(Value, NotAfterToday: true);

/// <summary>A day not before a date.</summary>
public sealed partial record Opened(DateOnly Value) : Day(Value, NotBefore: Opened.First)
{
    public static readonly DateOnly First = new(2030, 1, 1);
}

/// <summary>A moment that cannot be in the future.</summary>
public sealed partial record Fueled(DateTime Value) : Moment(Value, NotAfterNow: true);

/// <summary>A strict range of whole numbers.</summary>
public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);

/// <summary>A range bounded at both ends, tight at the top.</summary>
public sealed partial record Months(int From, int To) : Range(From, To, Min: 12, Max: 12);

/// <summary>A range of days, open at its end.</summary>
public sealed partial record Stay(DateOnly From, DateOnly? To) : Range(From, To, Strict: true);

/// <summary>A value object with a rule of its own over members of its own.</summary>
public sealed partial record Opening(DateOnly? Date, decimal? Quantity, Vin Vehicle) : Value
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

/// <summary>A value object whose own rule no sample satisfies.</summary>
public sealed partial record Never(string Name) : Value
{
    public const string Refusal = "Never.";

    protected override IEnumerable<Problem> Rules()
    {
        yield return Problem.Whole(Refusal);
    }
}
