namespace Rask.Site.Features.UiDocs.Examples;

// On a form, .Confirm asks before the submit, and the form is held only once Yes lets it go.
public sealed partial class ConfirmFormExample : Component
{
    private readonly Order _order = new();
    private string? _sent;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_order).OnSubmit(Send).Id("uc-form").Class("flex flex-wrap items-end gap-4")
                .Confirm("The kitchen starts cooking as soon as it arrives.", heading: "Send the order?")[
                Ui.Input.Bind(() => _order.Dish).Label("Dish").Id("uc-dish").Class("max-w-xs"),
                Ui.Button.Primary.Submit.Id("uc-send")["Send order"]
            ],
            Ui.Text.Id("uc-sent")[_sent is null ? "Nothing sent yet." : "Sent: " + _sent]
        ];

    private void Send(Order order) => _sent = order.Dish;

    public sealed class Order
    {
        public string Dish { get; set; } = "Pancakes";
    }
}
