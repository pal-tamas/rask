namespace Rask.SQLite.Browser;

/// <summary>
///     Whether this tab owns the browser database — resolve it and tell the user, rather than showing
///     them an empty page.
/// </summary>
/// <remarks>
///     <para>
///         Only one tab may own a browser database (every tab has its own copy of the in-memory
///         filesystem, so two owners would diverge and the last snapshot would win). The others run
///         against their own empty, unpersisted database — which, without something like this, looks
///         exactly like the user's data having been deleted. That is the worst possible reading of a
///         correct safety measure, and an app cannot correct it without being able to ask.
///     </para>
///     <para>
///         <see cref="IsOwner" /> is <see langword="null" /> until the answer is known, because "not
///         decided yet" and "not the owner" want different UI and collapsing them shows a scary banner
///         during a normal boot. Await <see cref="Resolved" /> to render only once it is settled.
///     </para>
///     <code>
///     protected override async Task OnMountAsync() =&gt; _isOwner = await ownership.Resolved;
///     </code>
/// </remarks>
public sealed class BrowserSqliteOwnership
{
    private readonly TaskCompletionSource<bool> _resolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     <see langword="true" /> if this tab owns the database, <see langword="false" /> if another tab
    ///     does, and <see langword="null" /> while the election is still in flight.
    /// </summary>
    public bool? IsOwner { get; private set; }

    /// <summary>Completes with the answer once the election has run, during the host's <c>StartAsync</c>.</summary>
    public Task<bool> Resolved => _resolved.Task;

    /// <summary>
    ///     For a tab that is <em>not</em> the owner: completes once the database has become free, so the
    ///     user can be told their data is reachable again rather than left guessing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Reloading is the only way to take it.</b> This tab already opened its own empty database
    ///         when it booted, and the app is holding live connections to it — the file cannot be swapped
    ///         underneath them, and a tab that started persisting its empty database would overwrite the
    ///         previous owner's good snapshot. So this signals "reload to use it", not "you now own it".
    ///     </para>
    ///     <para>
    ///         Advisory, not a claim: nothing is held on this tab's behalf, so another tab may take
    ///         ownership between this completing and the reload. The reloaded page runs the normal
    ///         election and finds out.
    ///     </para>
    ///     <para>Never completes in the owning tab, which has nothing to wait for.</para>
    ///     <code>
    ///     await ownership.Resolved;
    ///     if (ownership.IsOwner == false)
    ///     {
    ///         await ownership.Available;      // the other tab closed
    ///         _canReload = true; StateHasChanged();
    ///     }
    ///     </code>
    /// </remarks>
    public Task Available => _available.Task;

    private readonly TaskCompletionSource _available = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Idempotent: the election runs once per page, but a second call must not throw on the TCS.
    internal void Resolve(bool isOwner)
    {
        IsOwner = isOwner;
        _resolved.TrySetResult(isOwner);
    }

    internal void MarkAvailable() => _available.TrySetResult();
}
