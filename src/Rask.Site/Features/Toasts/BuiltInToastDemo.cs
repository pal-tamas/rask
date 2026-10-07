namespace Rask.Site.Features;

// Toasts are built in: nothing injected, nothing mounted — the host draws them in the kit's look.
public sealed partial class BuiltInToastDemo : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Ui.Button.Primary.Id("toast-save").OnClick(() => Toast.Success("Saved"))["Save"],
            Ui.Button.Id("toast-order").OnClick(() => Toast.Info("Your order was placed").Heading("Order 42"))["Place order"],
            Ui.Button.Error.Outline.Id("toast-fail")
                .OnClick(() => Toast.Error("Couldn't reach the server").UntilDismissed())["Fail"]
        ];
}
