namespace Rask.Ui;

/// <summary>Marks the subtree inside a <see cref="UiToaster" />, so a toast knows it is one of a stack.</summary>
/// <remarks>
/// A toast on its own places itself in a corner of the viewport. Inside a toaster the STACK is placed and each
/// toast fills its width, because two toasts that each pinned themselves to the same corner would sit on top
/// of one another.
/// </remarks>
internal sealed record UiToastStack;

/// <summary>
/// Where a page's toasts stack up.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's toaster. One per page, holding whatever toasts the page currently has — the page owns the list,
/// which is what makes a toast removable by the same render that added it:
/// </para>
/// <code>
/// UiToaster.Position(UiPosition.Top).Align(UiAlign.End)[
///     _notices.Select(n =&gt; UiToast.Key(n.Id).Message(n.Text).OnDismiss(() =&gt; _notices.Remove(n)))
/// ]
/// </code>
/// <para>
/// Newest LAST, reading downward, so the stack grows away from the corner it is pinned to and an arriving
/// toast never pushes the one being read out from under the reader's eye. It renders nothing at all when it
/// holds nothing, so an empty toaster costs a page no stray fixed element over its content.
/// </para>
/// </remarks>
public sealed partial class UiToaster : Component
{
    /// <summary>Which edge the stack is pinned to. The bottom, unless this says otherwise.</summary>
    public UiPosition? Position { get; set; }

    /// <summary>Where along that edge. Centred, unless this says otherwise.</summary>
    public UiAlign? Align { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Children is null
            ? null
            : Div
                .Class(UiClass.Compose(
                    "pointer-events-none fixed z-40 flex max-w-lg flex-col gap-2",
                    UiClassNames.ToastCorner(Position, Align),
                    Class))[
                // The stack ignores the pointer so it does not swallow clicks on the page underneath it
                // between toasts; each toast takes it back for itself.
                Div.Class("pointer-events-auto contents")[
                    Context.Provide(new UiToastStack())[Children]
                ]
            ];
}
