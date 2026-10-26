namespace Rask.Core.Components;

/// <summary>
///     Mounts what it wraps once it is scrolled into view, and shows a placeholder until then:
///     <c>Lazy.Placeholder(Ui.Skeleton)[SalesWidget]</c>. Livewire's lazy component, on both hosts.
/// </summary>
/// <remarks>
///     <para>
///         Until the reader scrolls to it, the wrapped component is not mounted: its <c>OnMount</c> and the reads it
///         starts do not run, on the first <c>GET</c> or after it. What is drawn in its place is
///         <see cref="Placeholder" /> inside a <c>div</c> the browser watches — an empty <c>div</c> when there is none,
///         as Livewire's. The moment any of it is in the viewport the browser says so, once, and the page answers
///         with the wrapped component mounted in the placeholder's place. A lazy inside it waits for its own turn.
///     </para>
///     <para>
///         The browser's word is nobody's action: it carries no bound value ahead of it. Each lazy is a frame of its
///         own, so a slow one does not hold a quick one back — what Livewire calls isolated.
///     </para>
/// </remarks>
public sealed class Lazy : Component
{
    private bool _inView;

    /// <summary>What stands in for the wrapped component until it is in view. Nothing by default.</summary>
    public Component? Placeholder { get; set; }

    /// <summary>Transparent once in view: the wrapped component stands where the placeholder stood.</summary>
    protected override string? TagName => null;

    /// <summary>What it stands for: built with the page, mounted only once the placeholder is in view.</summary>
    /// <param name="children">The wrapped components.</param>
    public new Lazy this[params Component?[] children]
    {
        get
        {
            HoldBack(children, !_inView);
            Children = children;
            return this;
        }
    }

    /// <inheritdoc />
    protected override Component? Render() =>
        _inView
            ? new Fragment { Children = Children ?? [] }
            : Div.Data("rask-lazy", string.Empty).OnLoad(InView)[Placeholder];

    // Raised by the browser (rask-lazy.ts) when the placeholder first crosses into the viewport.
    private void InView()
    {
        _inView = true;
        HoldBack(Children, held: false);
    }

    // A component the page builds is mounted when the page's render commits, drawn or not; what a lazy wraps,
    // at any depth, waits for the walk that draws it.
    private static void HoldBack(IEnumerable<Component?>? children, bool held)
    {
        if (children is null)
        {
            return;
        }

        foreach (var child in children.OfType<Component>())
        {
            child.HoldBackInternal(held);
            HoldBack(child.Children, held);
        }
    }
}
