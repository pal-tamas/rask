using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/toast</c> as it loads, example for example.
/// </summary>
/// <remarks>
///     <para>
///     That page shows no toast until a button is pressed, so what it loads as is ten examples of
///     <c>flux:button</c> — a component from another page. Each is a stand-in here, marked and held to the
///     size Flux measured (<c>data-parity-skip</c>), until <c>Ui.Button</c> is Flux's; then these become
///     <c>Ui.Button.OnClick(() =&gt; Toast.Success("…"))["Save changes"]</c>.
///     </para>
///     <para>
///     The toast those buttons raise is <see cref="ToastShownParity" />'s.
///     </para>
/// </remarks>
public sealed partial class ToastParity : FluxParity
{
    public override string Page => "toast";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // Flux::toast('Your changes have been saved.')               → Toast.Info("Your changes have been saved.")
        yield return ("", Row(Trigger("Save changes", 127.83)));

        // Flux::toast(heading: 'Changes saved', text: '…')           → Toast.Info("…").Heading("Changes saved")
        yield return ("with-heading", Row(Trigger("Save changes", 127.83)));

        // variant: 'success' | 'warning' | 'danger'                    → Toast.Success / Toast.Warning / Toast.Error
        yield return ("variants", Row(Trigger("Success", 90.94), Trigger("Warning", 89.89), Trigger("Danger", 82.8)));

        // <flux:toast invert />                                        → Ui.Toast.Invert()
        yield return ("inverted", Row(Trigger("Save changes", 127.83)));

        // action: ['label' => 'Undo', 'event' => 'undo-changes']       → .Action("Undo", UndoChanges)
        yield return ("actions", Row(Trigger("Save changes", 127.83)));

        // link: ['label' => 'View invoice', 'href' => …]               → .Link("View invoice", Routes.InvoicePage(id))
        yield return ("links", Row(Trigger("Create invoice", 130.11)));

        // <flux:toast position="top end" />                            → Ui.Toast.TopEnd
        yield return ("positioning", Row(Trigger("Save changes", 127.83)));

        // duration: 1000                                               → .For(1.Second)
        yield return ("duration", Row(Trigger("Save changes", 127.83)));

        // duration: 0                                                  → .UntilDismissed()
        yield return ("permanent", Row(Trigger("Save changes", 127.83)));

        // <flux:toast.group><flux:toast /></flux:toast.group>          → Ui.ToastGroup[Ui.Toast]
        yield return ("stack", Row(Trigger("Save changes", 127.83)));
    }

    private static Component Trigger(string label, double width) =>
        Button.Type(ButtonType.Button)
            .Style(FormattableString.Invariant($"width:{width}px;height:40px"))
            .Attributes(("data-ui-button", null), ("data-parity-skip", ""))[label];
}
