namespace Rask.Ui;

/// <summary>
/// A dialog: the whole story about one thing, without leaving the page it came from.
/// </summary>
/// <remarks>
/// <para>
/// A real <c>&lt;dialog&gt;</c>, and by default a <b>popover</b>. Set <see cref="Id" /> and give it a
/// <see cref="Trigger" /> and the browser owns the whole interaction: the top layer, so nothing on the
/// page can paint over it or trap it inside an <c>overflow: hidden</c> ancestor; Escape; light-dismiss;
/// and a real <c>::backdrop</c>, which daisyUI styles. None of that is implemented here, none of it
/// costs a line of script, and all of it works on a prerendered page before any runtime has booted.
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
/// What the state-driven path gives up is exactly what the popover buys: no top layer, and no focus
/// containment. Closing stays reachable by keyboard through the header button and the footer, but focus
/// is free to leave the dialog. A trap needs <c>showModal()</c>, which is script, and this kit ships
/// none — so where it matters, use the popover.
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

    /// <summary>Where it sits in the viewport.</summary>
    public UiModalPlacement? Placement { get; set; }

    /// <summary>
    ///     Runs on the close button and on a click outside. State-driven path only — on the popover path
    ///     the browser closes it and no callback is involved.
    /// </summary>
    public Action? Close { get; set; }

    /// <summary>The actions, trailing-aligned on a pointer and stacked on a phone.</summary>
    public Component? Footer { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Open is null && Id is { } id ? Popover(id) : StateDriven();

    private Component Popover(string id) =>
        // Two roots and no wrapper: the opener is a sibling of the dialog it names, so a caller can put
        // the trigger where it belongs in their own layout.
        [
            Trigger is { } trigger
                ? Button.Type("button").Class("btn").Attributes(("popovertarget", id))[trigger]
                : null,
            Shell(
                Dialog.Id(id).Class(Classes()).Popover("auto"),
                // The browser closes a popover from a button naming it, so the close control is markup
                // rather than a handler — and works with no runtime at all.
                Button
                    .Type("button")
                    .Class("btn btn-ghost btn-sm btn-square")
                    .Attributes(("popovertarget", id), ("popovertargetaction", "hide"))
                    .Aria(new Dictionary<string, string?> { ["label"] = "Close" })[
                    UiIcon.Name(UiIconName.Close).Class("size-4 shrink-0")
                ],
                backdrop: null)
        ];

    private Component StateDriven() =>
        Shell(
            // `open` on a <dialog> shows it non-modally; daisyUI's `.modal[open]` rule is what makes it
            // cover the viewport anyway. `modal-open` alongside it drives the transition.
            Dialog.Class(UiClass.Compose(Classes(), Open == false ? "" : "modal-open")).Open(Open != false),
            UiButton
                .Label("Close")
                .Variant(UiVariant.Ghost)
                .Size(UiSize.Sm)
                .Square(true)
                .Icon(UiIconName.Close)
                .OnClick(() => Close?.Invoke()),
            // A pointer convenience, not the only way out: the header's close button is the keyboard
            // path, which is why this carries no role and no label of its own.
            backdrop: Close is null
                ? null
                : Button
                    .Type("button")
                    .Class("modal-backdrop")
                    .Aria(new Dictionary<string, string?> { ["hidden"] = "true" })
                    .TabIndex(-1)
                    .OnClick(() => Close.Invoke())["close"]);

    private string Classes() =>
        UiClass.Compose(
            "modal",
            // The responsive default, and only where the caller has not chosen: a stated placement with
            // `sm:modal-middle` appended would be overridden at every width above a phone.
            Placement is { } placement
                ? UiClassNames.ModalPlacement(placement)
                : "modal-bottom sm:modal-middle",
            Class);

    // Takes the half-built chain rather than a finished component: only Build<T> carries the indexer
    // that adds children, and the two paths differ in how the dialog OPENS, not in what is inside it.
    private Component Shell(
        global::Rask.Core.Build<Dialog> dialog, Component closeControl, Component? backdrop) =>
        // No `role="dialog"`: the element IS a dialog and carries that role implicitly, so stating it
        // again is the redundant-ARIA that guidance tells you not to write. The NAME is not implicit,
        // though — a dialog with a heading inside is still an unnamed dialog to a screen reader, which
        // announces "dialog" and nothing else — so the title goes on as aria-label.
        dialog.Aria(new Dictionary<string, string?> { ["label"] = Title })[
            Div.Class("modal-box flex max-h-[88vh] flex-col p-0 sm:max-h-[85vh] sm:max-w-2xl")[
                Div.Class("flex items-start gap-3 border-b border-base-300 px-4 py-3 sm:px-5")[
                    H2.Class("min-w-0 grow break-words text-base font-semibold tracking-tight")[Title],
                    closeControl
                ],
                // The only scrolling region: the header and footer stay put while a stack trace moves.
                Div.Class("min-h-0 grow overflow-y-auto px-4 py-4 sm:px-5")[Children ?? []],
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
