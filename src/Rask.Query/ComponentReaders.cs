using Rask.Core;
using Rask.Core.Live;

namespace Rask.Querying;

/// <summary>
///     The components that read a piece of query state during a render, and are therefore owed a
///     re-render when it changes.
/// </summary>
/// <remarks>
///     <para>
///         Shared by <see cref="Query{TResult}" /> and the command types, which need exactly the same
///         thing: notice who is looking, tell them when it moves.
///     </para>
///     <para>
///         A render registers its component while a stream or a fetch, on another thread, may be telling them
///         all: the readers are an array replaced whole under a lock, so a change walks the ones that were
///         there and never a list half-way through an add.
///     </para>
/// </remarks>
internal sealed class ComponentReaders
{
    private readonly Lock _gate = new();
    private Reader[] _readers = [];
    private volatile bool _everObserved;

    /// <summary>Whether any component has ever read this during a render.</summary>
    /// <remarks>Set only once that reader is in the list, so whoever sees it set sees the reader too.</remarks>
    public bool EverObserved => _everObserved;

    /// <summary>Whether at least one registered component is still alive.</summary>
    public bool HasLiveReaders
    {
        get
        {
            foreach (var reader in Volatile.Read(ref _readers))
            {
                if (reader.Component.TryGetTarget(out _))
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
        if (LiveRenderContext.RenderOwner(out var pass) is not { } component)
        {
            return;
        }

        if (Find(Volatile.Read(ref _readers), component) is { } known)
        {
            known.LastReadIn = pass;
            return;
        }

        lock (_gate)
        {
            if (Find(_readers, component) is { } added)
            {
                added.LastReadIn = pass;
                return;
            }

            // Weak, so a component that leaves the tree without this being disposed — because it lives in
            // a longer-lived field — cannot be held alive by the cache.
            Volatile.Write(ref _readers, [.. _readers, new Reader(new WeakReference<Component>(component)) { LastReadIn = pass }]);
        }

        // After the add, never before: a value landing in between would find it observed with nobody reading, and
        // close the subscription under the component about to show it.
        _everObserved = true;
    }

    /// <summary>Re-renders every live reader, dropping any that has been collected.</summary>
    /// <remarks>
    ///     All but the component rendering now when it has not read this yet in that render: it will read the state as
    ///     it stands, and asking it to render again would only buy a second, identical frame. One that already read it
    ///     is told like any other — it is showing what this held before the change.
    /// </remarks>
    public void RenderAll()
    {
        var rendering = LiveRenderContext.RenderOwner(out var pass);
        var collected = false;
        foreach (var reader in Volatile.Read(ref _readers))
        {
            if (!reader.Component.TryGetTarget(out var component))
            {
                collected = true;
            }
            else if (!ReferenceEquals(component, rendering) || reader.LastReadIn == pass)
            {
                component.StateHasChanged();
            }
        }

        if (collected)
        {
            DropCollected();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Volatile.Write(ref _readers, []);
        }
    }

    private void DropCollected()
    {
        lock (_gate)
        {
            Volatile.Write(ref _readers, Array.FindAll(_readers, static reader => reader.Component.TryGetTarget(out _)));
        }
    }

    private static Reader? Find(Reader[] readers, Component component)
    {
        foreach (var reader in readers)
        {
            if (reader.Component.TryGetTarget(out var target) && ReferenceEquals(target, component))
            {
                return reader;
            }
        }

        return null;
    }

    /// <summary>One reading component, and the render pass it last read this in.</summary>
    private sealed class Reader(WeakReference<Component> component)
    {
        public WeakReference<Component> Component { get; } = component;

        public long LastReadIn { get; set; }
    }
}
