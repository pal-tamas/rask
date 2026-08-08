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

    // Idempotent: the election runs once per page, but a second call must not throw on the TCS.
    internal void Resolve(bool isOwner)
    {
        IsOwner = isOwner;
        _resolved.TrySetResult(isOwner);
    }
}
