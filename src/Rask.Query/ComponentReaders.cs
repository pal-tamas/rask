using Rask.Core;
using Rask.Core.Live;

namespace Rask.Querying;

/// <summary>
///     The components that read a piece of query state during a render, and are therefore owed a
///     re-render when it changes.
/// </summary>
/// <remarks>
///     Shared by <see cref="Query{TResult}" /> and the command types, which need exactly the same
///     thing: notice who is looking, tell them when it moves.
/// </remarks>
internal sealed class ComponentReaders
{
    private readonly List<WeakReference<Component>> _readers = [];

    /// <summary>Whether any component has ever read this during a render.</summary>
    public bool EverObserved { get; private set; }

    /// <summary>Whether at least one registered component is still alive.</summary>
    public bool HasLiveReaders
    {
        get
        {
            foreach (var reader in _readers)
            {
                if (reader.TryGetTarget(out _))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    ///     Registers the component currently rendering, if one is.
    /// </summary>
    /// <remarks>
    ///     The reader stays in the render cache: it renders again when <see cref="RenderAll" /> tells it the
    ///     state moved, and a walk some other component asked for serves its cached output.
    /// </remarks>
    public void Observe()
    {
        if (LiveRenderContext.RenderOwner(out _) is not { } component)
        {
            return;
        }

        EverObserved = true;

        foreach (var existing in _readers)
        {
            if (existing.TryGetTarget(out var target) && ReferenceEquals(target, component))
            {
                return;
            }
        }

        // Weak, so a component that leaves the tree without this being disposed — because it lives in
        // a longer-lived field — cannot be held alive by the cache.
        _readers.Add(new WeakReference<Component>(component));
    }

    /// <summary>Re-renders every live reader, dropping any that has been collected.</summary>
    /// <remarks>
    ///     All but the component rendering now, when this is said from inside a render: that one is reading
    ///     the state as it stands, and asking it to render again would only buy a second, identical frame.
    /// </remarks>
    public void RenderAll()
    {
        var rendering = LiveRenderContext.RenderOwner(out _);
        for (var i = _readers.Count - 1; i >= 0; i--)
        {
            if (!_readers[i].TryGetTarget(out var component))
            {
                _readers.RemoveAt(i);
            }
            else if (!ReferenceEquals(component, rendering))
            {
                component.StateHasChanged();
            }
        }
    }

    public void Clear() => _readers.Clear();
}
