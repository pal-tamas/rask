namespace Rask.Site.Features;

// Toasts are built in: nothing injected, nothing mounted — the host draws them in the kit's look.
public sealed partial class BuiltInToastDemo : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-2")[
            Ui.Button.Tone(Ui.Tone.Primary).Id("toast-save").OnClick(() => Toast.Success("Saved"))["Save"],
            Ui.Button.Id("toast-order").OnClick(() => Toast.Info("Your order was placed").Title("Order 42"))["Place order"],
            Ui.Button.Tone(Ui.Tone.Error).Variant(Ui.Variant.Outline).Id("toast-fail")
                .OnClick(() => Toast.Error("Couldn't reach the server").UntilDismissed())["Fail"]
        ];
}
