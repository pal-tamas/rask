namespace Rask.Ui;

/// <summary>
/// A dialog: the whole story about one thing, without leaving the page it came from.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI's <c>modal</c>, as a bottom sheet on a phone and a centred card from <c>sm</c> up
/// (<c>modal-bottom sm:modal-middle</c>). The sheet shape is not a stylistic choice: a centred dialog on a
/// 360px screen either overflows or shrinks its content to unreadable, and a stack trace is the one thing
/// here that must stay readable.
/// </para>
/// <para>
/// <b>It opens in one of two ways, and which one is chosen by whether <see cref="Id" /> is set.</b>
/// </para>
/// <para>
/// With an <see cref="Id" />, the dialog is <b>native</b>: it carries the <c>popover</c> attribute and is
/// opened by a button naming it through <c>popovertarget</c>. The browser then supplies the top layer,
/// Escape, light-dismiss and focus containment — none of which a page has to implement, and all of which
/// work on a prerendered page before any runtime has booted and with scripting off entirely. The id joins
/// the two halves, so it has to be unique on the page: two dialogs sharing one would give the first two
/// openers and the second none.
/// </para>
/// <para>
/// Without an <see cref="Id" />, the dialog is <b>state-driven</b>: the owning page renders it when its own
/// state says so and <see cref="Close" /> flips that state back. This is the path a component takes when
/// something in C# decides the dialog should appear — a row was selected, an action failed — which the
/// native path cannot express, because nothing in C# can press a button. What it does not buy is a focus
/// trap: closing is reachable by keyboard through the header button and the footer, but focus is free to
/// leave. A trap needs a key listener, and this kit ships no JavaScript of its own.
/// </para>
/// </remarks>
public sealed partial class UiModal : Component
{
    /// <summary>
    ///     Set it to get the native, script-free dialog; leave it unset to drive the dialog from the page's
    ///     own state. Must be unique on the page.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>daisyUI and MaryUI both call this <c>title</c>.</summary>
    public new required string Title { get; set; }

    /// <summary>
    ///     The label on the button that opens it. Native path only — with no <see cref="Id" /> there is
    ///     nothing for a button to target, and the page decides when the dialog appears.
    /// </summary>
    public string? Trigger { get; set; }

    /// <summary>
    ///     Runs on the close button and on a click outside the dialog. State-driven path only: on the
    ///     native path the browser closes it and no callback is involved.
    /// </summary>
    public Action? Close { get; set; }

    /// <summary>The actions, trailing-aligned on a pointer and stacked on a phone.</summary>
    public Component? Footer { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Id is { } id ? Native(id) : StateDriven();

    private Component Native(string id) =>
        // A collection expression, which the framework builds into its (internal) Fragment: two roots,
        // the opener and the dialog it names, with no wrapper element between them.
        [
            Trigger is { } trigger
                ? Button.Type("button").Class("btn").Attributes(("popovertarget", id))[trigger]
                : null,
            Div
                .Id(id)
                .Class(UiClass.Compose("modal modal-bottom sm:modal-middle", Class))
                .Popover("auto")[
                Box(
                    // The browser closes a popover from a button naming it, so the close control is markup
                    // rather than a handler — and works with no runtime at all.
                    Button
                        .Type("button")
                        .Class("btn btn-ghost btn-sm")
                        .Attributes(("popovertarget", id), ("popovertargetaction", "hide"))
                        .Aria(new Dictionary<string, string?> { ["label"] = "Close" })[
                        UiIcon.Name(UiIconName.Close).Class("size-4 shrink-0")
                    ])
            ]
        ];

    private Component StateDriven() =>
        Div.Class(UiClass.Compose("modal modal-open modal-bottom sm:modal-middle", Class))[
            Box(
                UiButton
                    .Label("Close")
                    .Variant(UiVariant.Ghost)
                    .Size(UiSize.Sm)
                    .Icon(UiIconName.Close)
                    .OnClick(() => Close?.Invoke())),
            // A pointer convenience, not the only way out: the header's close button is the keyboard path,
            // which is why this carries no role and no label of its own. daisyUI draws it as the backdrop.
            Close is null
                ? null
                : Button
                    .Type("button")
                    .Class("modal-backdrop")
                    .Aria(new Dictionary<string, string?> { ["hidden"] = "true" })
                    .Attributes(("tabindex", "-1"))
                    .OnClick(() => Close.Invoke())["close"]
        ];

    private Component Box(Component closeControl) =>
        Div
            .Role("dialog")
            .Aria(new Dictionary<string, string?> { ["modal"] = "true", ["label"] = Title })
            .Class("modal-box flex max-h-[88vh] flex-col p-0 sm:max-h-[85vh] sm:max-w-2xl")[
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
        ];
}

/// <summary>
/// The result of an action just taken, and the way to acknowledge it.
/// </summary>
/// <remarks>
/// Pinned to the bottom of the viewport rather than pushed into the page's flow. An inline notice moves
/// everything below it the moment an action completes, which on a phone means the list an operator was
/// reading jumps under their thumb; a toast reports the same thing and moves nothing.
/// <para>
/// <c>role="status"</c> rather than <c>alert</c>: this is the outcome of something the operator just did,
/// so it should be announced politely rather than interrupting.
/// </para>
/// </remarks>
public sealed partial class UiToast : Component
{
    public required string Message { get; set; }

    /// <summary><see cref="UiTone.Error" /> when the action failed. Anything else reads as done.</summary>
    public UiTone? Tone { get; set; }

    public Action? Dismiss { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Role("status")
            .Class(
                "fixed inset-x-3 bottom-3 z-40 mx-auto flex max-w-lg items-center gap-3 rounded-xl bg-ui-ink "
                + "px-4 py-3 text-sm text-ui-bg shadow-lg sm:inset-x-0")[
            // The FILL tokens, not the -ink twins, and amber rather than rose for the failure: this sits on
            // the near-black toast, where the light-ground text colours invert the problem they solve —
            // ui-danger on this ground is the low-contrast one. The icon shape (Warning vs Check) is what
            // actually carries the outcome; the colour only reinforces it.
            UiIcon
                .Name(Tone == UiTone.Error ? UiIconName.Warning : UiIconName.Check)
                .Class($"size-5 shrink-0 {(Tone == UiTone.Error ? "text-warning" : "text-success")}"),
            Span.Class("min-w-0 grow break-words")[Message],
            Dismiss is null
                ? null
                : Button
                    .Type("button")
                    .Class(
                        "-mr-1 shrink-0 rounded-lg px-2 py-1.5 text-xs font-medium text-ui-bg/70 "
                        + "hover:bg-base-100/10 hover:text-ui-bg")
                    .Aria(new Dictionary<string, string?> { ["label"] = "Dismiss" })
                    .OnClick(Dismiss)["Dismiss"]
        ];
}
