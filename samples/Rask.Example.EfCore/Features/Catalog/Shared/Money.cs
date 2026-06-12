namespace Rask.Example.EfCore.Features.Catalog.Shared;

// Value object for a price. The validation rule lives here and is reused verbatim by the inline
// form validator (Input(..., Validate: Money.Validate)) and by FromDecimal — one source of truth.
//
// Money is stored as integer minor units (cents). SQLite has no native decimal type: EF Core
// would map a decimal to a TEXT column and fall back to REAL for ORDER BY / aggregates, which is
// lossy and sorts lexicographically. An INTEGER cents column is exact, correctly sortable, and is
// the conventional way to model money — so the value object earns its keep.
public readonly record struct Money
{
    public const decimal MaxAmount = 1_000_000m;

    public long Cents { get; }

    private Money(long cents) => Cents = cents;

    public decimal Amount => Cents / 100m;

    // The single rule, shaped as Func<decimal, IEnumerable<string>> so the form can pass it
    // straight to Rask's inline Validate: parameter as a method group.
    public static IEnumerable<string> Validate(decimal amount)
    {
        if (amount <= 0m)
        {
            yield return "Price must be greater than zero.";
        }
        else if (amount > MaxAmount)
        {
            yield return $"Price must be {MaxAmount:N0} or less.";
        }
    }

    // Domain construction — enforces the same rule as a last line of defence.
    public static Money FromDecimal(decimal amount)
    {
        var errors = Validate(amount).ToList();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(amount));
        }

        return new Money((long)decimal.Round(amount * 100m, MidpointRounding.ToEven));
    }

    // Rehydration from the persisted INTEGER column (already-valid data).
    public static Money FromCents(long cents) => new(cents);

    public override string ToString() => Amount.ToString("C");
}
