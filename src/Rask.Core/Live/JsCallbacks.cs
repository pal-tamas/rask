using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Live;

/// <summary>
///     The callbacks the browser fires by id through a static <c>[JSInvokable]</c> (a watch, an observer, a
///     gesture's result). A static method cannot tell which session called it, and on the Server host one
///     process serves them all, so each entry remembers the <see cref="IJSRuntime" /> it was registered
///     through and answers only a call made by that same runtime (see <see cref="JsCaller" />). Without that,
///     any socket could post another user's watch id and feed their callback its own data.
/// </summary>
internal sealed class JsCallbacks<T>(int capacity = 0)
    where T : class
{
    private readonly ConcurrentDictionary<int, (IJSRuntime? Owner, T Handler)> _entries = new();
    private int _nextId;

    public int Register(IJSRuntime? owner, T handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        _entries[id] = (owner, handler);

        // A capped registry forgets the entry this many registrations back: by then its id has long left
        // every client's DOM, so it can never fire, and the map stays bounded.
        if (capacity > 0)
        {
            _entries.TryRemove(id - capacity, out _);
        }

        return id;
    }

    public void Unregister(int id) => _entries.TryRemove(id, out _);

    public bool TryGet(int id, [NotNullWhen(true)] out T? handler)
    {
        handler = _entries.TryGetValue(id, out var entry) && JsCaller.Owns(entry.Owner) ? entry.Handler : null;
        return handler is not null;
    }

    /// <summary>A one-shot lookup: removes the entry, but only for the runtime that owns it.</summary>
    public bool TryTake(int id, [NotNullWhen(true)] out T? handler)
    {
        handler = null;
        if (!_entries.TryGetValue(id, out var entry) || !JsCaller.Owns(entry.Owner))
        {
            return false;
        }

        if (!_entries.TryRemove(new KeyValuePair<int, (IJSRuntime?, T)>(id, entry)))
        {
            return false;
        }

        handler = entry.Handler;
        return true;
    }
}

/// <summary>
///     Which runtime is calling into .NET right now. The Server host names the session's runtime around each
///     <c>dotNetInvoke</c> it dispatches; <c>DotNetDispatcher</c> runs the target method synchronously on that
///     thread, so the lookup inside it sees the caller.
/// </summary>
internal static class JsCaller
{
    [ThreadStatic] private static IJSRuntime? _current;

    public static Scope Enter(IJSRuntime runtime)
    {
        var previous = _current;
        _current = runtime;
        return new Scope(previous);
    }

    // No caller named means a process with one user behind it (the browser host) or a direct call from
    // .NET; either way there is no other session to protect.
    public static bool Owns(IJSRuntime? owner) => _current is not { } caller || ReferenceEquals(caller, owner);

    public readonly struct Scope(IJSRuntime? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}
