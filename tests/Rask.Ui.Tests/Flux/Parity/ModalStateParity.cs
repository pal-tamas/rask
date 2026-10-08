using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     The modals of <see cref="ModalParity" /> with the PAGE owning whether each is open — Rask's
///     <c>wire:model</c> — for <c>scripts/flux/parity-modal.mjs</c> to open the way a render does.
/// </summary>
/// <remarks>
///     Flux's page shows no bound modal, so there is nothing of its own to pair these with: each is held to
///     the same example's dialog as its trigger opens it on Flux's page. The script changes
///     <c>data-rask-modal-open</c> — all a render changes — and the runtime does the rest, so a dialog the
///     page's state opens has to be the same modal, in the same place, behind the same backdrop.
/// </remarks>
public sealed partial class ModalStateParity : FluxParity
{
    public override string Page => "modal-state";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", ModalParity.Centre(Div[Ui.Modal.Open(false).Class("parity-w-96")[ModalParity.Profile()]]));

        yield return ("confirmation", ModalParity.Centre(Ui.Modal.Open(false).Class("parity-min-w-22")[ModalParity.Confirmation()]));

        yield return ("flyout", ModalParity.Centre(Ui.Modal.Open(false).Flyout().Class("parity-w-lg")[ModalParity.Profile()]));

        yield return ("flyout-positioning", ModalParity.Centre(
            Ui.Modal.Open(false).Flyout().Floating.Class("parity-w-lg")[ModalParity.FloatingProfile()]));
    }
}
