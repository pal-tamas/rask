using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     Flux's confirmation with what an app really writes in it: a question for a heading, no text under it,
///     two buttons — with the example's own width and without it. For <c>scripts/flux/parity-modal.mjs</c>.
/// </summary>
/// <remarks>
///     <para>
///     Flux's page shows none of these, so the script makes each one there: it takes the confirmation example,
///     drops its <c>min-w-[22rem]</c> where the case is a bare modal, writes the question into its heading and
///     removes its text. The cases here are in the script's order (<c>CONFIRM</c>), one example each.
///     </para>
///     <para>
///     What they hold: how wide a modal with no width of its own is, where the close button sits against the
///     corner, and how near the first line of the question comes to it. Flux's heading keeps no room for the
///     button, so a line that fills the panel runs under it — there and here alike.
///     </para>
///     <para>
///     Last is <c>Ui.ConfirmLeave</c>, which is not Flux's: the script opens it with the same questions and
///     holds its close button to Flux's corner and its question clear of the button.
///     </para>
/// </remarks>
public sealed partial class ModalConfirmParity : FluxParity
{
    internal const string Question = "Biztos elhagyod mentés nélkül az oldalt?";

    internal const string LongQuestion = "Biztos elhagyod mentés nélkül az oldalt, és eldobod, amit eddig beírtál az űrlapba?";

    public override string Page => "modal-confirm";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("bare-question", Case("bare-question", null, Question));
        yield return ("bare-short", Case("bare-short", null, "Delete project?"));
        yield return ("confirmation-question", Case("confirmation-question", "parity-min-w-22", Question));
        yield return ("bare-long", Case("bare-long", null, LongQuestion));
        yield return ("confirmation-long", Case("confirmation-long", "parity-min-w-22", LongQuestion));
        yield return ("confirm-leave", ModalParity.Centre(Ui.ConfirmLeave));
    }

    private static Component Case(string name, string? width, string question) =>
        ModalParity.Centre(
            Ui.ModalTrigger.Name(name)[Ui.Button.Danger["Delete"]],
            Ui.Modal.Name(name).Class(width)[Asked(question)]);

    // Flux's confirmation without its text: the heading keeps the block it stood in.
    private static Component Asked(string question) =>
        SpaceY(6,
            Div[Ui.Heading.Lg[question]],
            Div.Style("display:flex;gap:8px")[
                Div.Data("parity-skip", "").Style("flex:1 1 0%"),
                Ui.ModalClose[Ui.Button.Ghost["Cancel"]],
                Ui.Button.Danger.Type(Ui.ButtonType.Submit)["Delete project"]
            ]);
}
