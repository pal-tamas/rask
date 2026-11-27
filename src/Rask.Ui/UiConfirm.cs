namespace Rask;

/// <summary>
///     The dialog an action's <c>.Confirm("…")</c> asks in, in place of the browser's own <c>confirm</c> and
///     <c>prompt</c>. Put one in the layout and every <c>.Confirm</c> on every page asks through it — and so does
///     <see cref="Ask" />, from code.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Confirm                                          // "Confirm", "No" and "Yes"
///     Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")
///     Ui.Confirm.Primary()                                // Yes as the primary button: no question here is a danger
///     Ui.Button.Danger.OnClick(Delete).Confirm("Biztosan törli a céget?", heading: "Cég törlése")   // its own title
///
///     if (!await Ui.Confirm.Ask("The period overlaps another. Save it anyway?")) return;           // from code
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

    // The two looks Yes has, as the runtime names them (data-rask-confirm-tone).
    private const string PrimaryTone = "primary";
    private const string DangerTone = "danger";

    /// <summary>The short title above the question. "Confirm" when unset; a question's own <c>heading:</c> replaces it.</summary>
    public string? Heading { get; set; }

    /// <summary>The label of the button that calls the action off. "No" when unset.</summary>
    public string? No { get; set; }

    /// <summary>The label of the button that lets the action go. "Yes" when unset.</summary>
    public string? Yes { get; set; }

    /// <summary>
    ///     Yes as Flux's primary button rather than its danger one, for a question that is not about losing something:
    ///     <c>Ui.Confirm.Primary()</c>. Unset, Yes is the danger button.
    /// </summary>
    public bool? Primary { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        UiConfirmation.Dialog(
            DialogId,
            Part,
            UiConfirmation.Retitled(Heading ?? RaskStrings.Get(RaskString.ConfirmHeading, "Confirm"), Part, MessageId),
            Answer(),
            NoButton(No ?? RaskStrings.Get(RaskString.ConfirmNo, "No")),
            Go(Yes ?? RaskStrings.Get(RaskString.ConfirmYes, "Yes")));

    // Yes in the dialog's own tone, carrying the other tone's look for a question asked in that one: the runtime
    // puts it on while that question is asked (rask-confirm-dialog.ts), and the dialog's own back after.
    private Component Go(string text)
    {
        var primary = Primary == true;
        // The other tone's classes as a button with words has them, not as an icon's square.
        var other = (primary ? Ui.Button.Danger : Ui.Button.Primary).Square(false);
        return UiConfirmation.Go(
            primary ? Ui.Button.Primary : Ui.Button.Danger,
            DialogId,
            Part,
            [("data-rask-confirm-tone", primary ? PrimaryTone : DangerTone), ("data-rask-confirm-other", other.DrawnClass), .. Worded(text)])[text];
    }

    // Flux's Cancel holds its words and nothing around them.
    private static Component NoButton(string text) =>
        Ui.Button.Ghost.Data(Part, "no").Attributes(Worded(text))[text];

    // A button's label, which a question asked from code may word for itself: the runtime writes it, and puts back
    // the layout's, kept in data-rask-confirm-default — out of the morph's hands, as the heading is.
    private static (string Name, string? Value)[] Worded(string text) =>
        [("data-rask-confirm-default", text), ("data-rask-opaque", null)];

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
