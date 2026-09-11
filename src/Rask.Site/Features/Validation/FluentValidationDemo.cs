using FluentValidation;

namespace Rask.Site.Features;

public sealed partial class FluentValidationDemo : Component
{
    private readonly OrderModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Ordered {m.Quantity} × {m.Product}").Class("flex flex-col gap-3")[
            Div[
                UiInput.Bind(() => _model.Product).Label("Product").Id("v7-product").ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Product)
            ],
            Div[
                UiInput.Bind(() => _model.Quantity).Label("Quantity").Id("v7-quantity").ShowValidation(false),
                ValidationMessage.Template(FieldError).For(() => _model.Quantity)
            ],
            Div[
                UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit)[UiIcon.Name(UiIconName.ShoppingBag), "Order"]
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle), _submission]
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
