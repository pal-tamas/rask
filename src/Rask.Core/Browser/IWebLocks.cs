using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>How a <see cref="IWebLocks" /> lock is held — see the Web Locks API's <c>mode</c> option.</summary>
public enum LockMode
{
    /// <summary>Only one holder at a time (the default). Waits for any current holder to release.</summary>
    Exclusive,

    /// <summary>Any number of shared holders concurrently, but never alongside an exclusive holder.</summary>
    Shared,
}

/// <summary>
///     One entry from <see cref="IWebLocks.QueryAsync" /> — a lock that is currently held or waiting.
/// </summary>
/// <param name="Name">The lock name.</param>
/// <param name="Mode">Requested mode, <c>"exclusive"</c> or <c>"shared"</c> (the raw API string).</param>
/// <param name="ClientId">An opaque id for the browsing context that holds/requested it, when reported.</param>
/// <param name="Held"><c>true</c> if this lock is currently granted; <c>false</c> if it is still pending.</param>
public sealed record LockInfo(string Name, string Mode, string? ClientId, bool Held);

/// <summary>
///     Typed access to the Web Locks API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Locks_API" />) — coordinate work
///     across the tabs, windows, and workers of one origin by acquiring a named lock, doing work while it's
///     held, and releasing it. Useful to serialise something that must not run twice at once (a token
///     refresh, an IndexedDB migration, a "leader" tab). Works on <b>both transports</b> — it needs no user
///     gesture — so inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         The lock is held only for the lifetime of the callback you pass: <see cref="RequestAsync" />
///         waits until the lock is free, runs your <c>work</c>, then releases — even if <c>work</c> throws.
///         <see cref="TryRequestAsync" /> returns immediately with <c>false</c> (without running <c>work</c>)
///         if the lock is already held. Keep the work reasonably short; other contexts block on an exclusive
///         lock until you return. There is no timeout or cancellation — waiting for a lock nothing releases
///         waits forever, so prefer <see cref="TryRequestAsync" /> when you can't guarantee progress. On the
///         Server transport the lock is held across a WS round-trip; if the connection drops mid-hold, the
///         browser keeps the grant until that page/context is torn down.
///     </para>
///     <code>
///     // Only one tab refreshes the token at a time; the others wait, then see the fresh value.
///     await locks.RequestAsync("token-refresh", async () =&gt; { await RefreshTokenAsync(); });
///
///     // "Leader tab" — the first tab wins the lock and keeps it; later tabs get false and stand down.
///     var isLeader = await locks.TryRequestAsync("leader", async () =&gt; { await RunLeaderLoopAsync(); });
///     </code>
/// </remarks>
public interface IWebLocks
{
    /// <summary>Whether the browser supports the Web Locks API (<c>"locks" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Acquires the lock <paramref name="name" /> (waiting for any current holder to release), runs
    ///     <paramref name="work" /> while holding it, then releases it — releasing even if <paramref name="work" />
    ///     throws, in which case the exception propagates.
    /// </summary>
    ValueTask RequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive);

    /// <summary>
    ///     Tries to acquire the lock <paramref name="name" /> <em>without waiting</em> (the API's
    ///     <c>ifAvailable</c>). If it's free, runs <paramref name="work" /> while holding it, releases, and
    ///     returns <c>true</c>. If it's already held, returns <c>false</c> immediately and does not run
    ///     <paramref name="work" />.
    /// </summary>
    ValueTask<bool> TryRequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive);

    /// <summary>
    ///     Snapshots the locks currently held and pending for this origin (<c>navigator.locks.query()</c>).
    ///     A diagnostic aid; the set can change the moment it's read.
    /// </summary>
    ValueTask<IReadOnlyList<LockInfo>> QueryAsync();
}

/// <summary>
///     Default <see cref="IWebLocks" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>Lock</c> is opaque to C#, so the framework's <c>__raskLocks</c> helper holds it under a
///     C#-minted id: <c>request</c> resolves as soon as the lock is granted (or <c>false</c> when
///     <c>ifAvailable</c> can't grant it), and the helper keeps the lock until <c>release</c> is called —
///     which this wrapper does once <c>work</c> completes. No <c>[JSInvokable]</c> callback is needed:
///     C# controls the hold purely by when it calls <c>release</c>.
/// </summary>
public sealed class WebLocks(IJSRuntime js) : IWebLocks
{
    private static int _nextId;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskLocks.isSupported");

    /// <inheritdoc />
    public async ValueTask RequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(work);

        // A non-ifAvailable request resolves true once the lock is granted (it waits as long as needed).
        var id = Interlocked.Increment(ref _nextId);
        await js.InvokeAsync<bool>("__raskLocks.request", id, name, ModeString(mode), false);
        try
        {
            await work();
        }
        finally
        {
            await js.InvokeVoidAsync("__raskLocks.release", id);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryRequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(work);

        var id = Interlocked.Increment(ref _nextId);
        var granted = await js.InvokeAsync<bool>("__raskLocks.request", id, name, ModeString(mode), true);
        if (!granted)
        {
            return false; // ifAvailable: the lock was already held — work never runs, nothing to release.
        }

        try
        {
            await work();
        }
        finally
        {
            await js.InvokeVoidAsync("__raskLocks.release", id);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LockInfo>> QueryAsync()
    {
        var locks = await js.InvokeAsync<LockInfo[]>("__raskLocks.query");
        return locks ?? [];
    }

    // Map the enum to the raw API string C#-side, so nothing enum-shaped crosses the JS bridge.
    private static string ModeString(LockMode mode) => mode == LockMode.Shared ? "shared" : "exclusive";
}
