using System.Globalization;

namespace Rask;

/// <summary>
/// The result of an action just taken, and the way to acknowledge it.
/// </summary>
/// <remarks>
/// <para>
/// Pinned to the viewport rather than pushed into the page's flow. An inline notice moves everything below it
/// the moment an action completes, which on a phone means the list an operator was reading jumps under their
/// thumb; a toast reports the same thing and moves nothing.
/// </para>
/// <para>
/// <c>role="status"</c> rather than <c>alert</c>: this is the outcome of something the operator just did, so
/// it should be announced politely rather than interrupting. An <see cref="Ui.Tone.Error" /> toast is the
/// exception and says <c>alert</c>, because a failure the reader is about to act on cannot wait for a pause.
/// </para>
/// <para>
/// <b>The page owns the list.</b> One toast is one component; several live inside a <see cref="UiToaster" />,
/// which stacks them in a corner. <see cref="Duration" /> asks the runtime to dismiss it after a while — by
/// clicking its own dismiss control, so the page's <see cref="OnDismiss" /> runs and the page removes it from
/// its own state. Hiding it in the DOM instead would leave the page believing a toast is up that nobody can
/// see, and the next render would put it back.
/// </para>
/// </remarks>
public sealed partial class UiToast : Component
{
    public required string Message { get; set; }

    /// <summary>A stronger first line above the message — what happened, with the message saying more.</summary>
    public string? Heading { get; set; }

    /// <summary><see cref="Ui.Tone.Error" /> when the action failed. Anything else reads as done.</summary>
    public Ui.Tone? Tone { get; set; }

    /// <summary>Runs when the reader acknowledges it. Without one the toast has no dismiss button.</summary>
    public Callback OnDismiss { get; set; }

    /// <summary>
    ///     Something to do about it — an Undo, a link to what was created. One control, at the end of the row.
    /// </summary>
    /// <remarks>
    ///     A toast is a passing thing, so what it offers has to be reachable before it goes: give it a
    ///     <see cref="Duration" /> long enough to act on, or none at all. The runtime pauses the countdown
    ///     while the pointer is over the toast or focus is inside it, so reaching for this does not lose it.
    /// </remarks>
    public Component? Action { get; set; }

    /// <summary>
    ///     How long it stays before dismissing itself. It stays until acknowledged unless this says otherwise.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Needs an <see cref="OnDismiss" />, because that is HOW it dismisses: the runtime clicks the toast's
    ///     own dismiss control rather than removing the element, so the page's handler runs and the page takes
    ///     the toast out of its own state. Without one there is nothing to click and the toast stays.
    ///     </para>
    ///     <para>
    ///     The countdown pauses while the pointer is over the toast or focus is inside it. A notice that
    ///     disappears while somebody is reading it, or mid-way through reaching for its <see cref="Action" />,
    ///     is worse than one that stays.
    ///     </para>
    /// </remarks>
    public TimeSpan? Duration { get; set; }

    /// <summary>Which corner it sits in, when it is not inside a <see cref="UiToaster" />.</summary>
    /// <remarks>Ignored inside a toaster, which places the whole stack and lets each toast fill its width.</remarks>
    public Ui.Position? Position { get; set; }

    /// <inheritdoc cref="Position" />
    public Ui.Align? Align { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var stacked = Context.Has<UiToastStack>();

        var toast = Div
            // A failure is the one outcome worth interrupting for: the reader is usually about to act on the
            // thing that just failed.
            .Role(Tone == Ui.Tone.Error ? "alert" : "status")
            .Class(UiClass.Compose(
                "flex items-center gap-3 rounded-xl bg-ui-ink px-4 py-3 text-sm text-ui-bg shadow-lg",
                stacked ? "w-full" : "fixed z-40 mx-auto max-w-lg " + UiClassNames.ToastCorner(Position, Align),
                Class));

        if (Duration is { } duration && OnDismiss.HasValue)
        {
            // The runtime dismisses it by clicking the control below, so the PAGE's handler runs — see the
            // remarks on Duration.
            toast = toast.Data(
                "rask-dismiss-after",
                ((long)duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        }

        return toast[
            // The FILL tokens, not the -ink twins, and amber rather than rose for the failure: this sits on
            // the near-black toast, where the light-ground text colours invert the problem they solve —
            // ui-danger on this ground is the low-contrast one. The icon shape (Warning vs Check) is what
            // actually carries the outcome; the colour only reinforces it.
            Ui.Icon
                .Name(Tone == Ui.Tone.Error ? Ui.IconName.Warning : Ui.IconName.Check)
                .Class($"size-5 shrink-0 {(Tone == Ui.Tone.Error ? "text-warning" : "text-success")}"),
            Heading is { Length: > 0 } heading
                ? Div.Class("flex min-w-0 grow flex-col gap-0.5")[
                    Span.Class("font-medium")[heading],
                    Span.Class("break-words opacity-80")[Message]
                ]
                : Span.Class("min-w-0 grow break-words")[Message],
            Action,
            !OnDismiss.HasValue
                ? null
                : Button
                    .Type("button")
                    .Class(
                        "-mr-1 shrink-0 rounded-lg px-2 py-1.5 text-xs font-medium text-ui-bg/70 "
                        + "hover:bg-base-100/10 hover:text-ui-bg")
                    // data-rask-dismiss: the runtime's own convention for "the control that closes this", the
                    // same one the focus trap presses on Escape — and what Duration clicks.
                    .Attributes(("data-rask-dismiss", null))
                    .Aria(new Dictionary<string, string?> { ["label"] = "Dismiss" })
                    .OnClick(OnDismiss)["Dismiss"]
        ];
    }
}
