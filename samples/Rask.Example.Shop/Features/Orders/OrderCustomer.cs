namespace Rask.Example.Shop.Features.Orders;

// Value object for Customer — the validation rule lives here and is reused by the form
// (Input(...).Validate(OrderCustomer.Validate)) and by Create.
public readonly record struct OrderCustomer
{
    public const int MaxLength = 200;

    public string Value { get; }

    private OrderCustomer(string value) => Value = value;

    public static IEnumerable<string> Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return "Customer is required.";
        }
        else if (value.Trim().Length > MaxLength)
        {
            yield return $"Customer must be {MaxLength} characters or fewer.";
        }
    }

    public static OrderCustomer Create(string value)
    {
        var errors = Validate(value).ToList();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(value));
        }

        return new OrderCustomer(value.Trim());
    }

    public override string ToString() => Value;
}
