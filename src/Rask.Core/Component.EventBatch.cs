namespace Rask.Core;

public abstract partial class Component
{
    /// <summary>
    ///     Whether the handler <paramref name="id" /> names is the one a render made now would register again.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Asked on the render root between two events of a batch, when the earlier ones have changed the page
    ///         and not rendered it yet. A render re-runs the components that are dirty and, through changed props,
    ///         the ones below them — and a component that runs again makes its handlers again, closing over what
    ///         that render computed. So a handler whose component, and every component above it, is clean is the
    ///         same delegate after the render as before it: running it first changes nothing it can see.
    ///     </para>
    ///     <para>
    ///         Anything else answers <c>false</c> and the host renders first, exactly as it did before events were
    ///         batched: a dirty component on the way up, an id the page does not have (the render may be what
    ///         brings it), or a component the last walk did not reach.
    ///     </para>
    /// </remarks>
    /// <param name="id">The handler id the browser sent.</param>
    /// <param name="parents">The tree as last rendered, child to parent — from <see cref="MapParents" />.</param>
    internal bool HandlerOutlivesRender(string id, Dictionary<Component, Component> parents)
    {
        if (Live.Handlers is null || !Live.Handlers.TryGetValue(id, out var entry))
        {
            return false;
        }

        var component = entry.Owner;
        while (component._live is not { StateDirty: true } and not { PropsDirty: true })
        {
            if (ReferenceEquals(component, this))
            {
                return true;
            }

            if (!parents.TryGetValue(component, out var parent))
            {
                return false;
            }

            component = parent;
        }

        return false;
    }

    /// <summary>Fills <paramref name="parents" /> with this tree as it was last rendered, child to parent.</summary>
    internal void MapParents(Dictionary<Component, Component> parents)
    {
        if (_live?.Children is null)
        {
            return;
        }

        foreach (var child in _live.Children.Values)
        {
            child.MapUnder(this, parents);
        }
    }

    // An instance met twice keeps the parent it was first met under, and is not walked again.
    private void MapUnder(Component parent, Dictionary<Component, Component> parents)
    {
        if (parents.TryAdd(this, parent))
        {
            MapParents(parents);
        }
    }
}
