namespace Rask.Example.EfCore.Features.Catalog.Shared;

// Value object for a product's name. As with Money, the rule lives on the value object and is
// reused by the inline form validator (Input(..., Validate: ProductName.Validate)) and by From.
public readonly record struct ProductName
{
    public const int MaxLength = 120;

    public string Value { get; }

    private ProductName(string value) => Value = value;

    public static IEnumerable<string> Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return "Name is required.";
        }
        else if (value.Trim().Length > MaxLength)
        {
            yield return $"Name must be {MaxLength} characters or fewer.";
        }
    }

    public static ProductName From(string value)
    {
        var errors = Validate(value).ToList();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(value));
        }

        return new ProductName(value.Trim());
    }

    public override string ToString() => Value;
}
