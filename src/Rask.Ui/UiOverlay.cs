namespace Rask.Ui;

/// <summary>
/// A dialog: the whole story about one thing, without leaving the page it came from.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI's <c>modal</c>, as a bottom sheet on a phone and a centred card from <c>sm</c> up unless
/// <see cref="Placement" /> says otherwise. The sheet shape is not a stylistic default: a centred dialog
/// on a 360px screen either overflows or shrinks its content to unreadable, and a stack trace is the one
/// thing here that must stay readable.
/// </para>
/// <para>
/// <b>The page owns whether it is showing.</b> Render it when your state says so and let
/// <see cref="Close" /> flip that state back, or keep it rendered and drive <see cref="Open" />. Left
/// unset, <see cref="Open" /> means open — rendering a dialog at all is the ordinary way to ask for one,
/// and a component that rendered nothing visible by default would be a trap.
/// </para>
/// <para>
/// <b>This used to have a second, native path</b>, chosen by setting an <c>Id</c>: the dialog carried the
/// <c>popover</c> attribute and a button opened it through <c>popovertarget</c>, so the browser supplied
/// the top layer, Escape, light-dismiss and focus containment with no script at all. That was genuinely
/// better on every axis except one, and the exception is what removed it: nothing in C# can press a
/// button, so a dialog opened that way could not be opened, closed or even observed by the page that
/// owned it. Two paths with different capabilities also meant two sets of behaviour to document and to
/// test, distinguished only by whether a property happened to be set.
/// </para>
/// <para>
/// What the state-driven path does not buy is a focus trap. Closing is reachable by keyboard through the
/// header button and the footer, but focus is free to leave the dialog. A trap needs a key listener, and
/// this kit ships no JavaScript of its own.
/// </para>
/// </remarks>
public sealed partial class UiModal : Component
{
    /// <summary>daisyUI and MaryUI both call this <c>title</c>.</summary>
    public new required string Title { get; set; }

    /// <summary>
    ///     Whether the dialog is showing. Unset means showing — see the remarks. Set it to <c>false</c> to
    ///     keep the dialog rendered but hidden, which is what you want when its content is expensive to
    ///     rebuild or must not lose its scroll position.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>Where it sits in the viewport.</summary>
    public UiModalPlacement? Placement { get; set; }

    /// <summary>Runs on the close button and on a click outside the dialog.</summary>
    public Action? Close { get; set; }

    /// <summary>The actions, trailing-aligned on a pointer and stacked on a phone.</summary>
    public Component? Footer { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "modal",
            Open == false ? "" : "modal-open",
            // The responsive default, and only when the caller has not chosen: a stated placement that
            // then had `sm:modal-middle` appended would be overridden at every width above a phone.
            Placement is { } placement
                ? UiClassNames.ModalPlacement(placement)
                : "modal-bottom sm:modal-middle",
            Class))[
            Div
                .Role("dialog")
                .Aria(new Dictionary<string, string?> { ["modal"] = "true", ["label"] = Title })
                .Class("modal-box flex max-h-[88vh] flex-col p-0 sm:max-h-[85vh] sm:max-w-2xl")[
                Div.Class("flex items-start gap-3 border-b border-base-300 px-4 py-3 sm:px-5")[
                    H2.Class("min-w-0 grow break-words text-base font-semibold tracking-tight")[Title],
                    UiButton
                        .Label("Close")
                        .Variant(UiVariant.Ghost)
                        .Size(UiSize.Sm)
                        .Square(true)
                        .Icon(UiIconName.Close)
                        .OnClick(() => Close?.Invoke())
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
            // A pointer convenience, not the only way out: the header's close button is the keyboard path,
            // which is why this carries no role and no label of its own. daisyUI draws it as the backdrop.
            Close is null
                ? null
                : Button
                    .Type("button")
                    .Class("modal-backdrop")
                    .Aria(new Dictionary<string, string?> { ["hidden"] = "true" })
                    .TabIndex(-1)
                    .OnClick(() => Close.Invoke())["close"]
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
