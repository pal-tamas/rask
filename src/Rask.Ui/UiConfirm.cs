namespace Rask;

/// <summary>
///     The dialog an action's <c>.Confirm("…")</c> asks in, in place of the browser's own <c>confirm</c> and
///     <c>prompt</c>. Put one in the layout and every <c>.Confirm</c> on every page asks through it.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Confirm                                          // "Confirm", "No" and "Yes"
///     Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")
///     </code>
///     <para>
///     Not a Flux component: a <see cref="UiModal" /> composed as Flux's confirmation, drawn as
///     <see cref="UiConfirmLeave" /> is. The heading is short; the call site's question is the sentence beneath it.
///     <see cref="No" />, the close button, Escape and a press outside call the action off; only <see cref="Yes" />
///     lets the click or the submit go. With <c>expect:</c> the reader types the answer into a field, and Yes waits
///     for an exact match, case and all.
///     </para>
///     <para>
///     All of it happens in the browser: opening, typing and closing send the server nothing. Without this element
///     <c>.Confirm</c> asks with <c>window.confirm</c> and <c>window.prompt</c>.
///     </para>
/// </remarks>
public sealed partial class UiConfirm : Component
{
    /// <summary>The dialog's id: one per page, which is what a layout places.</summary>
    internal const string DialogId = "ui-confirm";

    // What the runtime looks for (rask-confirm-dialog.ts): the question, the field an answer is typed into, and yes.
    private const string Part = "rask-confirm-dialog";

    private const string MessageId = DialogId + "-message";

    /// <summary>The short title above the question. "Confirm" when unset.</summary>
    public string? Heading { get; set; }

    /// <summary>The label of the button that calls the action off. "No" when unset.</summary>
    public string? No { get; set; }

    /// <summary>The label of the button that lets the action go. "Yes" when unset.</summary>
    public string? Yes { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        UiConfirmation.Dialog(
            DialogId,
            Part,
            UiConfirmation.Titled(Heading ?? RaskStrings.Get(RaskString.ConfirmHeading, "Confirm"), Part, MessageId),
            Answer(),
            no: No ?? RaskStrings.Get(RaskString.ConfirmNo, "No"),
            yes: Yes ?? RaskStrings.Get(RaskString.ConfirmYes, "Yes"));

    // Shown only for `expect:`, and holding no value of the page's: what is typed in it is never sent.
    private static Component Answer() =>
        Div.Hidden().Data(Part, "field")[
            Ui.Input.Of<string>().Attributes(
                ("aria-labelledby", MessageId),
                ("autocomplete", "off"),
                ("spellcheck", "false"),
                UiRuntimeMarks.NotFormData)
        ];
}
