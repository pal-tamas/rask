namespace Rask.Example.EfCore.Features.Catalog.Shared;

// Value object for on-hand stock. Same pattern: the rule lives here and is reused by the inline
// form validator (Input(..., Validate: StockLevel.Validate)) and by From.
public readonly record struct StockLevel
{
    public int Value { get; }

    private StockLevel(int value) => Value = value;

    public static IEnumerable<string> Validate(int value)
    {
        if (value < 0)
        {
            yield return "Stock cannot be negative.";
        }
    }

    public static StockLevel From(int value)
    {
        var errors = Validate(value).ToList();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(value));
        }

        return new StockLevel(value);
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
