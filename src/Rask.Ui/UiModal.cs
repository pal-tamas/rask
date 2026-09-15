namespace Rask.Ui;

/// <summary>
/// A dialog: the whole story about one thing, without leaving the page it came from.
/// </summary>
/// <remarks>
/// <para>
/// A real <c>&lt;dialog&gt;</c>, and by default a real <b>modal</b> one. Set <see cref="Id" /> and give it a
/// <see cref="Trigger" /> and the browser owns the whole interaction. The trigger is an invoker —
/// <c>command="show-modal"</c> — so the dialog opens the way <c>showModal()</c> opens it, with no script:
/// the top layer, so nothing on the page can paint over it or trap it inside an <c>overflow: hidden</c>
/// ancestor; the page behind made inert, so Tab cannot wander out of it; Escape; and focus handed back to
/// the trigger on close. None of that is implemented here, and all of it works on a prerendered page
/// before any runtime has booted.
/// </para>
/// <para>
/// The same buttons also name the dialog as a <c>popover</c>. A browser without invoker commands (before
/// Chrome 135, Firefox 144, Safari 26.2) ignores <c>command</c> and opens it as a popover instead — still
/// in the top layer, still closed by Escape — just without the inert page behind it. A page is never left
/// with a button that does nothing.
/// </para>
/// <para>
/// <b>The state-driven path is the exception, not the default.</b> Set <see cref="Open" /> and the
/// dialog stops being a popover and becomes an ordinary <c>&lt;dialog open&gt;</c> that the page
/// renders when its own state says so. Reach for it when something in C# decides the dialog should
/// appear — a row was selected, an action failed — which the declarative path cannot express, because
/// nothing in C# can press a button.
/// </para>
/// <para>
/// The two are mutually exclusive in the markup and have to be: a <c>[popover]</c> element is
/// <c>display: none</c> until the browser shows it, so a <c>modal-open</c> class on one would set a
/// class that changes nothing. Setting <see cref="Open" /> is therefore what chooses the path.
/// </para>
/// <para>
/// What the state-driven path gives up is the top layer: nothing in markup can put an element there. It
/// keeps focus containment, through the runtime's <c>data-rask-focus-trap</c> — focus moves in when it opens,
/// Tab cycles inside it, Escape runs <see cref="OnClose" />, and focus returns to what had it when it closes.
/// </para>
/// <para>
/// A dialog locks the page's scroll while it is open, from the kit's stylesheet, so a long page does not
/// slide underneath a sheet a reader is trying to scroll.
/// </para>
/// </remarks>
public sealed partial class UiModal : Component
{
    /// <summary>daisyUI and MaryUI both call this <c>title</c>.</summary>
    public required string Title { get; set; }

    /// <summary>
    ///     Names the dialog so a button can open it. Required for the popover path — it is what
    ///     <c>popovertarget</c> refers to — and must be unique on the page: two dialogs sharing one
    ///     would give the first two openers and the second none.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    ///     The label on the button that opens it. Needs <see cref="Id" />; with no id there is nothing
    ///     for a button to name.
    /// </summary>
    public string? Trigger { get; set; }

    /// <summary>
    ///     Takes the dialog off the popover path and hands the open state to the page. Leave it unset to
    ///     let the browser own it, which is the better default — see the remarks.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>
    ///     Where it sits in the viewport. <see cref="UiModalPosition.Start" /> and <see cref="UiModalPosition.End" />
    ///     make it a full-height flyout from that edge — Flux UI's flyout — rather than a centred box.
    /// </summary>
    public UiModalPosition? Position { get; set; }

    /// <summary>
    ///     Runs when it closes. On the state-driven path that is the close button, a click outside and Escape,
    ///     and it is how the page learns to stop rendering it open. On the modal path the browser closes it
    ///     and this hears that it did.
    /// </summary>
    public Callback? OnClose { get; set; }

    /// <summary>Whether a click outside closes it. On unless this is <see langword="false" />.</summary>
    /// <remarks>
    ///     Turn it off for a dialog holding work a stray click would lose. A browser without invoker commands
    ///     opens the modal path as a manual popover when this is off, which also means Escape no longer closes
    ///     it there — the close button still does.
    /// </remarks>
    public bool? Dismissible { get; set; }

    /// <summary>Whether Escape closes it. On unless this is <see langword="false" />.</summary>
    /// <remarks>
    ///     Off writes <c>closedby="none"</c> on the modal path — Safari has not shipped it yet and still closes on
    ///     Escape — and drops the runtime's Escape handling on the state-driven path. A dialog that cannot be
    ///     escaped needs a visible way out, so leave <see cref="Closable" /> on or put one in the footer.
    /// </remarks>
    public bool? Escapable { get; set; }

    /// <summary>Whether the header shows a close button. On unless this is <see langword="false" />.</summary>
    public bool? Closable { get; set; }

    /// <summary>The actions, trailing-aligned on a pointer and stacked on a phone.</summary>
    public Component? Footer { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Open is null && Id is { } id ? Popover(id) : StateDriven();

    private Component Popover(string id)
    {
        // Both names on every control, so each browser takes the one it has: an invoker command where it is
        // supported, which the platform acts on first, and the popover action everywhere else.
        (string, string?)[] Opens() => [("command", "show-modal"), ("commandfor", id), ("popovertarget", id)];
        (string, string?)[] Closes() =>
            [("command", "close"), ("commandfor", id), ("popovertarget", id), ("popovertargetaction", "hide")];

        var dialog = Dialog
            .Id(id)
            .Class(Classes())
            // Manual where the fallback must not light-dismiss or escape: an auto popover does both.
            .Popover(Dismissible == false || Escapable == false ? "manual" : "auto");

        if (Escapable == false)
        {
            dialog = dialog.Attributes(("closedby", "none"));
        }

        if (OnClose is { } onClose)
        {
            // The dialog's own toggle event: the platform reports every way it closed, the ones no handler here
            // saw included — Escape, the backdrop, a button inside the body.
            dialog = dialog.OnToggle(e => e.IsOpen ? Task.CompletedTask : onClose.Invoke() ?? Task.CompletedTask);
        }

        // Two roots and no wrapper: the opener is a sibling of the dialog it names, so a caller can put
        // the trigger where it belongs in their own layout.
        return
        [
            Trigger is { } trigger
                ? Button.Type("button").Class("btn").Attributes(Opens())[trigger]
                : null,
            Shell(
                dialog,
                // Markup rather than a handler, so it closes with no runtime at all.
                Closable == false
                    ? null
                    : Button
                        .Type("button")
                        .Class("btn btn-ghost btn-sm btn-square")
                        .Attributes(Closes())
                        .Aria(new Dictionary<string, string?> { ["label"] = "Close" })[
                        UiIcon.Name(UiIconName.Close).Class("size-4 shrink-0")
                    ],
                // A MODAL dialog has no light-dismiss of its own in most browsers — the viewport-sized `.modal`
                // is the dialog, so a click on the dimmed area is a click inside it. daisyUI's backdrop button is
                // the part that click lands on. No role and no label: the close button is the keyboard's way out.
                backdrop: Dismissible == false
                    ? null
                    : Button
                        .Type("button")
                        .Class("modal-backdrop")
                        .Aria(new Dictionary<string, string?> { ["hidden"] = "true" })
                        .TabIndex(-1)
                        .Attributes(Closes())["close"])
        ];
    }

    private Component StateDriven()
    {
        var open = Open != false;

        // `open` on a <dialog> shows it non-modally; daisyUI's `.modal[open]` rule is what makes it cover the
        // viewport anyway. `modal-open` alongside it drives the transition.
        var dialog = Dialog.Class(UiClass.Compose(Classes(), open ? "modal-open" : "")).Open(open);

        // Containment comes from the runtime's focus trap, only while it is open — on a closed dialog that
        // stays mounted the attribute's removal is what hands focus back.
        if (open)
        {
            dialog = dialog.TabIndex(-1).Attributes(("data-rask-focus-trap", null));
        }

        var close = UiButton
            .AccessibleLabel("Close")
            .Variant(UiVariant.Ghost)
            .Size(UiSize.Sm)
            .Square(true)
            .OnClick(() => OnClose?.Invoke() ?? Task.CompletedTask);

        // The trap presses the [data-rask-dismiss] control on Escape.
        if (Escapable != false && OnClose is not null)
        {
            close = close.Attributes(("data-rask-dismiss", null));
        }

        return Shell(
            dialog,
            Closable == false ? EscapeTarget() : close[UiIcon.Name(UiIconName.Close)],
            // A pointer convenience, not the only way out: the header's close button is the keyboard
            // path, which is why this carries no role and no label of its own.
            backdrop: OnClose is null || Dismissible == false
                ? null
                : Button
                    .Type("button")
                    .Class("modal-backdrop")
                    .Aria(new Dictionary<string, string?> { ["hidden"] = "true" })
                    .TabIndex(-1)
                    .OnClick(() => OnClose?.Invoke() ?? Task.CompletedTask)["close"]);
    }

    // With no close button in the header, Escape still needs a control to press: a hidden one, out of the tab
    // order and the accessibility tree, that runs OnClose the same way the visible button would have.
    private Component? EscapeTarget() =>
        Escapable == false || OnClose is null
            ? null
            : Button
                .Type("button")
                .Class("hidden")
                .Aria(new Dictionary<string, string?> { ["hidden"] = "true" })
                .TabIndex(-1)
                .Attributes(("data-rask-dismiss", null))
                .OnClick(() => OnClose?.Invoke() ?? Task.CompletedTask)["close"];

    private bool IsFlyout => Position is UiModalPosition.Start or UiModalPosition.End;

    private string Classes() =>
        UiClass.Compose(
            "modal",
            // The responsive default, and only where the caller has not chosen: a stated placement with
            // `sm:modal-middle` appended would be overridden at every width above a phone.
            Position is { } position
                ? UiClassNames.ModalPosition(position)
                : "modal-bottom sm:modal-middle",
            Class);

    // Takes the dialog itself. It used to take `Build<Dialog>`, because the chain receiver was the only
    // thing carrying the children indexer; the component carries it now. The two paths differ in how the
    // dialog OPENS, not in what is inside it.
    private Component Shell(Dialog dialog, Component? closeControl, Component? backdrop) =>
        // No `role="dialog"`: the element IS a dialog and carries that role implicitly, so stating it
        // again is the redundant-ARIA that guidance tells you not to write. The NAME is not implicit,
        // though — a dialog with a heading inside is still an unnamed dialog to a screen reader, which
        // announces "dialog" and nothing else — so the title goes on as aria-label.
        dialog.Aria(new Dictionary<string, string?> { ["label"] = Title })[
            Div.Class(IsFlyout
                ? "modal-box flex w-[min(28rem,100vw)] flex-col p-0"
                // daisyUI's side modals are already full height; the centred box's height and width caps would
                // override that and turn the flyout back into a floating box.
                : "modal-box flex max-h-[88vh] flex-col p-0 sm:max-h-[85vh] sm:max-w-2xl")[
                Div.Class("flex items-start gap-3 border-b border-base-300 px-4 py-3 sm:px-5")[
                    H2.Class("min-w-0 grow break-words text-base font-semibold tracking-tight")[Title],
                    closeControl
                ],
                // The only scrolling region: the header and footer stay put while a stack trace moves.
                Div.Class("min-h-0 grow space-y-4 overflow-y-auto px-4 py-4 sm:px-5")[Children ?? []],
                Footer is null
                    ? null
                    : Div.Class(
                        "flex flex-col-reverse gap-2 border-t border-base-300 px-4 py-3 sm:flex-row "
                        + "sm:justify-end sm:px-5")[
                        Footer
                    ]
            ],
            backdrop
        ];
}
