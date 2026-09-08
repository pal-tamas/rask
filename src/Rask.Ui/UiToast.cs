namespace Rask.Ui;

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
