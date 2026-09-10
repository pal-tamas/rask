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
                Label.For("v7-product").Class($"{Tw.Label} text-sm mb-1")["Product"],
                Input.Bind(() => _model.Product).Id("v7-product").Class(Tw.Input),
                ValidationMessage.Template(FieldError).For(() => _model.Product)
            ],
            Div[
                Label.For("v7-quantity").Class($"{Tw.Label} text-sm mb-1")["Quantity"],
                Input.Bind(() => _model.Quantity).Id("v7-quantity").Class(Tw.Input),
                ValidationMessage.Template(FieldError).For(() => _model.Quantity)
            ],
            Div[
                UiButton.Label("Order").Icon(UiIconName.ShoppingBag).Tone(UiTone.Primary).Type(UiButtonType.Submit)
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Icon(UiIconName.CheckCircle).Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[_submission]
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
