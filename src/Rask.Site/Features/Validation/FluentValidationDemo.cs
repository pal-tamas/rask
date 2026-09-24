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
        Form.Model(_model).OnSubmit(m => _submission = $"Ordered {m.Quantity} × {m.Product}").Class("flex flex-col gap-3")[
            Div[
                Ui.Input.Bind(() => _model.Product).Label("Product").Id("v7-product").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Product)
            ],
            Div[
                Ui.Input.Bind(() => _model.Quantity).Label("Quantity").Id("v7-quantity").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Quantity)
            ],
            Div[
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.ShoppingBag), "Order"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
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
