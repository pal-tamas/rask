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
        yield return ("", Div[Trigger("Save changes")]);

        // Flux::toast(heading: 'Changes saved', text: '…')           → Toast.Info("…").Heading("Changes saved")
        yield return ("with-heading", Div[Trigger("Save changes")]);

        // variant: 'success' | 'warning' | 'danger'                    → Toast.Success / Toast.Warning / Toast.Error
        yield return ("variants", Row(Trigger("Success"), Trigger("Warning"), Trigger("Danger")));

        // <flux:toast invert />                                        → Ui.Toast.Invert()
        yield return ("inverted", Div[Trigger("Save changes")]);

        // action: ['label' => 'Undo', 'event' => 'undo-changes']       → .Action("Undo", UndoChanges)
        // The one example whose button is `wire:click`: it carries the loading indicator, at rest.
        yield return ("actions", Div[Ui.Button.OnClick(static () => { })["Save changes"]]);

        // link: ['label' => 'View invoice', 'href' => …]               → .Link("View invoice", Routes.InvoicePage(id))
        yield return ("links", Div[Trigger("Create invoice")]);

        // <flux:toast position="top end" />                            → Ui.Toast.TopEnd
        yield return ("positioning", Div[Trigger("Save changes")]);

        // duration: 1000                                               → .For(1.Second)
        yield return ("duration", Div[Trigger("Save changes")]);

        // duration: 0                                                  → .UntilDismissed()
        yield return ("permanent", Div[Trigger("Save changes")]);

        // <flux:toast.group><flux:toast /></flux:toast.group>          → Ui.ToastGroup[Ui.Toast]
        yield return ("stack", Div[Trigger("Save changes")]);
    }

    private static Component Trigger(string label) => Ui.Button[label];
}
