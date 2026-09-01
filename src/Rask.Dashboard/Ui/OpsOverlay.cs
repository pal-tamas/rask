namespace Rask.Dashboard.Pages;

/// <summary>
/// A detail sheet: the whole story about one row, without leaving the list it came from.
/// </summary>
/// <remarks>
/// <para>
/// A bottom sheet on a phone and a centred card from <c>sm</c> up. The sheet shape is not a stylistic
/// choice — a centred dialog on a 360px screen either overflows or shrinks its content to unreadable, and
/// a stack trace is the one thing here that must stay readable.
/// </para>
/// <para>
/// Open is a state flip on the owning page, exactly as the confirmation prompt already was: no dialog API,
/// no script, and it works identically on the Server transport and in WASM. What that does NOT buy is a
/// focus trap — closing is reachable by keyboard through the header's close button and the footer, but
/// focus is free to leave the sheet. A trap needs a key listener, and the console ships no JavaScript.
/// </para>
/// </remarks>
internal sealed partial class OpsModal : Component
{
    public required string Heading { get; set; }

    /// <summary>Runs on the close button and on a click outside the sheet.</summary>
    public Action? Close { get; set; }

    /// <summary>The actions, trailing-aligned on a pointer and stacked on a phone.</summary>
    public Component? Footer { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4")[
            // A pointer convenience, not the only way out: the header's close button is the keyboard path,
            // which is why this one carries no role and no label of its own.
            CloseSurface(),
            Div.Role("dialog")
                .Aria(new Dictionary<string, string?> { ["modal"] = "true", ["label"] = Heading })
                .Class(
                    "relative flex max-h-[88vh] w-full flex-col rounded-t-2xl border border-ops-line bg-ops-bg "
                    + "shadow-xl sm:max-h-[85vh] sm:max-w-2xl sm:rounded-2xl")[
                Div.Class("flex items-start gap-3 border-b border-ops-line px-4 py-3 sm:px-5")[
                    H2.Class("min-w-0 grow break-words text-base font-semibold tracking-tight text-ops-ink")[Heading],
                    OpsButton
                        .Label("Close")
                        .Tone("quiet")
                        .Icon(OpsIconName.Close)
                        .Class("!px-2")
                        .OnClick(Dismiss)
                ],
                // The only scrolling region: the header and footer stay put while a stack trace moves.
                Div.Class("min-h-0 grow overflow-y-auto px-4 py-4 sm:px-5")[Children ?? []],
                Footer is null
                    ? null
                    : Div.Class(
                        "flex flex-col-reverse gap-2 border-t border-ops-line px-4 py-3 sm:flex-row "
                        + "sm:justify-end sm:px-5")[
                        Footer
                    ]
            ]
        ];

    private Component CloseSurface() =>
        Div.Class("absolute inset-0 bg-black/30").OnClick(Dismiss);

    private void Dismiss() => Close?.Invoke();
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
internal sealed partial class OpsToast : Component
{
    public required string Message { get; set; }

    /// <summary><c>danger</c> when the action failed. Anything else reads as done.</summary>
    public string? Tone { get; set; }

    public Action? Dismiss { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Role("status")
            .Class(
                "fixed inset-x-3 bottom-3 z-40 mx-auto flex max-w-lg items-center gap-3 rounded-xl bg-ops-ink "
                + "px-4 py-3 text-sm text-ops-bg shadow-lg sm:inset-x-0")[
            // The FILL tokens, not the -ink twins, and amber rather than rose for the failure: this sits on
            // the near-black toast, where the light-ground text colours invert the problem they solve —
            // ops-danger on this ground is the low-contrast one. The icon shape (Warning vs Check) is what
            // actually carries the outcome; the colour only reinforces it.
            OpsIcon
                .Name(Tone == "danger" ? OpsIconName.Warning : OpsIconName.Check)
                .Class($"size-5 shrink-0 {(Tone == "danger" ? "text-ops-warn" : "text-ops-ok")}"),
            Span.Class("min-w-0 grow break-words")[Message],
            Dismiss is null
                ? null
                : Button
                    .Type("button")
                    .Class(
                        "-mr-1 shrink-0 rounded-lg px-2 py-1.5 text-xs font-medium text-ops-bg/70 "
                        + "hover:bg-ops-bg/10 hover:text-ops-bg")
                    .Aria(new Dictionary<string, string?> { ["label"] = "Dismiss" })
                    .OnClick(Dismiss)["Dismiss"]
        ];
}
