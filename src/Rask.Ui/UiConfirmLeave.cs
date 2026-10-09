namespace Rask;

/// <summary>
///     The dialog a form's <c>ConfirmLeave("…")</c> asks in, in place of the browser's own <c>confirm</c>. Put
///     one in the layout and every guarded form on every page asks through it.
/// </summary>
/// <remarks>
///     <code>
///     Ui.ConfirmLeave                                // "Stay" and "Leave"
///     Ui.ConfirmLeave.Stay("Nem").Leave("Igen")      // your own words
///     </code>
///     <para>
///     Not a Flux component: a <see cref="UiModal" /> composed as a confirmation, so it is drawn and behaves
///     as every other modal does. Its text is the asking form's own message. <see cref="Stay" />, the close
///     button, Escape and a press outside all keep the reader on the page, exactly as it was;
///     <see cref="Leave" /> carries on with the navigation that was asked for.
///     </para>
///     <para>
///     It is rendered closed and opened in the browser, because what it guards is what the server has not seen
///     yet. Closing the tab, a reload and a link out of the app still get the browser's own prompt, which no
///     page can replace. Without this element a guarded form asks with <c>window.confirm</c>.
///     </para>
/// </remarks>
public sealed partial class UiConfirmLeave : Component
{
    /// <summary>The dialog's id: one per page, which is what a layout places.</summary>
    internal const string DialogId = "ui-confirm-leave";

    // What the runtime looks for (rask-leave.ts): where the form's message goes, and the button that leaves.
    private const string Part = "rask-leave";

    /// <summary>The label of the button that keeps the reader on the page. "Stay" when unset.</summary>
    public string? Stay { get; set; }

    /// <summary>The label of the button that leaves, dropping what was typed. "Leave" when unset.</summary>
    public string? Leave { get; set; }

    /// <summary>
    ///     A short title above the form's question, as Flux titles a confirmation ("Delete project?" over a
    ///     sentence). Unset, the question itself is the title.
    /// </summary>
    public string? Heading { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // The width Flux's own confirmation writes. A modal hugs what it holds, there and here.
        Ui.Modal.Name(DialogId).Class("min-w-[22rem]")[
            Div.Class("space-y-6")[
                Question(),
                Div.Class("flex gap-2")[
                    Ui.Spacer,
                    Ui.ModalClose.Key("stay")[Ui.Button.Ghost[Stay ?? RaskStrings.Get(RaskString.ConfirmLeaveStay, "Stay")]],
                    Ui.ModalClose.Key("leave")[Ui.Button.Danger.Data(Part, "go")[Leave ?? RaskStrings.Get(RaskString.ConfirmLeaveLeave, "Leave")]]
                ]
            ]
        ];

    // The question is empty in every render and filled in the browser, so it is kept out of the morph's hands: a
    // render that lands while the dialog is open would otherwise blank it. Flux's heading keeps no room for the
    // close button, because its examples keep a heading short: this one may be a whole question, so it keeps
    // the room itself (measured by parity-modal.mjs).
    private Component Question() => Heading is { } heading
        ? Div[
            Ui.Heading.Level(2).Lg.Class("pe-8")[heading],
            Ui.Text.Class("mt-2").Data(Part, "message").Attributes(("data-rask-opaque", null))
        ]
        : Ui.Heading.Level(2).Lg.Class("pe-8").Data(Part, "message").Attributes(("data-rask-opaque", null));
}
