namespace Rask.Core.Browser;

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
