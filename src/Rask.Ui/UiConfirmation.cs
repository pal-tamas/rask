using static Rask.Markup;

namespace Rask;

/// <summary>
///     The confirmation the kit's own questions are drawn as, <see cref="UiConfirmLeave" /> and <see cref="UiConfirm" />:
///     a <see cref="UiModal" /> shaped as Flux's confirmation example — the question, then No and Yes in a row.
/// </summary>
/// <remarks>
///     Rendered closed, with the question empty: the runtime writes it and opens the dialog (<c>rask-ask.ts</c>). The
///     parts it looks for carry <c>data-{part}</c>: <c>message</c> where the question goes, <c>go</c> on the button that
///     says yes. Every other way the dialog closes is no.
/// </remarks>
internal static class UiConfirmation
{
    /// <summary>The dialog: <paramref name="question" />, an optional <paramref name="field" />, and the two buttons.</summary>
    internal static Component Dialog(string id, string part, Component question, Component? field, string no, string yes) =>
        // The width Flux's own confirmation writes. A modal hugs what it holds, there and here.
        Ui.Modal.Name(id).Class("min-w-[22rem]")[
            Div.Class("space-y-6")[
                question,
                field,
                Div.Class("flex gap-2")[
                    Ui.Spacer,
                    Ui.ModalClose.Key("no")[Ui.Button.Ghost[no]],
                    // Flux's own: the button that says yes stands in the row by itself, and closes the dialog.
                    Ui.Button.Danger.Key("yes").Data(part, "go").Attributes(UiModalInvoker.Closes(id))[yes]
                ]
            ]
        ];

    // The question is empty in every render and filled in the browser, so it is kept out of the morph's hands: a
    // render that lands while the dialog is open would otherwise blank it.

    /// <summary>A short title, and the question as the sentence beneath it — Flux's "Delete project?" over its text.</summary>
    internal static Component Titled(string heading, string part, string? messageId = null) =>
        Div[
            Ui.Heading.Lg[heading],
            Ui.Text.Id(messageId).Class("mt-2").Data(part, "message").Attributes(("data-rask-opaque", null))
        ];

    // Flux's heading keeps no room for the close button, because its examples keep a heading short: a whole
    // question may run under it, so this one keeps the room itself (measured by parity-modal.mjs).

    /// <summary>The question alone, as the title.</summary>
    internal static Component Untitled(string part) =>
        Ui.Heading.Level(2).Lg.Class("pe-8").Data(part, "message").Attributes(("data-rask-opaque", null));
}
