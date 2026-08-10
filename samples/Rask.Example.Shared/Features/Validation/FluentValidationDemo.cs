using FluentValidation;

namespace Rask.Example.Shared.Features;

public sealed partial class FluentValidationDemo : Component
{
    private readonly OrderModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger small mt-1")[m])];

    protected override Component? Render() =>
    [
        Form<OrderModel>(
            _model,
            m => _submission = $"Ordered {m.Quantity} × {m.Product}",
            Class: "vstack gap-3")[
            FluentValidationValidator.Validator(new OrderValidator()),
            Div[
                Label.For("v7-product").Class("form-label small mb-1")["Product"],
                Input(() => _model.Product).Id("v7-product").Class("form-control"),
                ValidationMessage(() => _model.Product, FieldError)
            ],
            Div[
                Label.For("v7-quantity").Class("form-label small mb-1")["Quantity"],
                Input(() => _model.Quantity).Id("v7-quantity").Class("form-control"),
                ValidationMessage(() => _model.Quantity, FieldError)
            ],
            Div[
                BsButton.Type("submit").Color(BsColor.Primary)[BsIcon.Name(BsIconName.BagCheck).Class("me-1"), "Order"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert.Color(BsColor.Success).Class("small mt-3 mb-0")[BsIcon.Name(BsIconName.CheckCircle).Class("me-2"), _submission]
    ];
}

public sealed class OrderModel
{
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class OrderValidator : AbstractValidator<OrderModel>
{
    public OrderValidator()
    {
        RuleFor(x => x.Product).NotEmpty().WithMessage("Product is required.");
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
    }
}
