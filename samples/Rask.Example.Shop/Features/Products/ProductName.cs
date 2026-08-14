namespace Rask.Example.Shop.Features.Products;

// Value object for Name — the validation rule lives here and is reused by the form
// (Input(...).Validate(ProductName.Validate)) and by Create.
public readonly record struct ProductName
{
    public const int MaxLength = 200;

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

    public static ProductName Create(string value)
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
