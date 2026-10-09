using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>The title the route's pages declare, and the components that show it.</summary>
internal sealed class RouteTitle
{
    // Every component that read the title while rendering. A page is mounted where its layout places the
    // Outlet, so a reader has always rendered before the title of the page below it could be known.
    private readonly HashSet<Component> _readers = new(ReferenceEqualityComparer.Instance);
    private string? _value;

    // What the walk in progress has read off the pages so far. Kept here rather than on the render
    // context, which every render of every app allocates and only a routed one would use.
    private string? _walked;

    /// <summary>Starts collecting the title of a new walk.</summary>
    public void BeginWalk() => _walked = null;

    /// <summary>
    ///     Takes a page's title. The chain is walked layout first, so the deepest page that declares one is
    ///     the last to offer.
    /// </summary>
    public void Offer(string title) => _walked = title;

    /// <summary>Publishes what the walk collected; see <see cref="Publish" />.</summary>
    public bool Settle() => Publish(_walked);

    public string? Read()
    {
        if (LiveRenderContext.CurrentSync is { } ctx)
        {
            _readers.Add(ctx.WalkParent);
        }

        return _value;
    }

    /// <summary>
    ///     Takes the title the walk that just ended read off the route's pages, and marks every reader to
    ///     render again when it differs.
    /// </summary>
    /// <returns>Whether a reader now shows the old title, so the tree has to be walked once more.</returns>
    public bool Publish(string? value)
    {
        var changed = !string.Equals(_value, value, StringComparison.Ordinal);
        _value = value;

        // Swept on every frame rather than only on a change: a reader that left must not be kept for as
        // long as the title happens to stay the same. One or two entries, and nothing allocated.
        var stale = false;
        foreach (var reader in _readers)
        {
            if (reader.IsTornDown)
            {
                _readers.Remove(reader);
            }
            else if (changed)
            {
                reader.MarkDirtyForFrame();
                stale = true;
            }
        }

        return stale;
    }
}
