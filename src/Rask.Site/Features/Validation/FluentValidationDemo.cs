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
                Ui.Button.Primary.Icon(Ui.IconName.ShoppingBag).Submit["Order"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_submission)
    ];
}
