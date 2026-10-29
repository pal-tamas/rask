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
    private readonly Action? _lastReaderGone;
    private Reader[] _readers = [];
    private volatile bool _everObserved;

    /// <summary>Readers of state that stops nothing, or of state whose work stops when its last reader leaves.</summary>
    /// <param name="lastReaderGone">
    ///     Called when a reader leaves the page and no reader is left on it — what the reads were for has gone, so the
    ///     work behind them can stop. Null for state nothing has to stop for.
    /// </param>
    public ComponentReaders(Action? lastReaderGone = null) => _lastReaderGone = lastReaderGone;

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

        Reader reader;
        lock (_gate)
        {
            if (Find(_readers, component) is { } added)
            {
                added.LastReadIn = pass;
                return;
            }

            // Weak, so a component that leaves the tree without this being disposed — because it lives in
            // a longer-lived field — cannot be held alive by the cache.
            reader = new Reader(new WeakReference<Component>(component)) { LastReadIn = pass };
            Volatile.Write(ref _readers, [.. _readers, reader]);
        }

        // Outside the lock: a component already gone runs the callback here and now.
        if (_lastReaderGone is not null)
        {
            reader.Gone = component.CallbackLifetime.Register(static state => ((ComponentReaders)state!).ReaderGone(), this);
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
        Reader[] cleared;
        lock (_gate)
        {
            cleared = _readers;
            Volatile.Write(ref _readers, []);
        }

        foreach (var reader in cleared)
        {
            reader.Gone.Dispose();
        }
    }

    /// <summary>Whether any reader is still on the page: alive, and not unmounted.</summary>
    private bool AnyReaderMounted()
    {
        foreach (var reader in Volatile.Read(ref _readers))
        {
            if (reader.Component.TryGetTarget(out var component) && !component.IsTornDown)
            {
                return true;
            }
        }

        return false;
    }

    // One reader left the page. Every reader of a shared query has to be gone before the work stops: another may
    // still be showing it.
    private void ReaderGone()
    {
        if (!AnyReaderMounted())
        {
            _lastReaderGone?.Invoke();
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

        /// <summary>The watch on the component's lifetime, released with the reader.</summary>
        public CancellationTokenRegistration Gone { get; set; }
    }
}
