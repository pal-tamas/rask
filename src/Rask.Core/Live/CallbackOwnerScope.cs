namespace Rask.Core.Live;

/// <summary>
///     Runs a callback under the lifetime of the component that wrote it, until the scope is disposed.
/// </summary>
/// <remarks>
///     <para>
///         A callback is its owner's code reached through another component: an editor's save button runs the
///         page's <c>OnSaved</c>. The page's code often unmounts the very component that invoked it — it
///         closes the editor, then reloads — so the reload must be cancelled with the page, not with the editor.
///     </para>
///     <para>
///         Only the cancellation changes hands. The services, the user, the navigator's handler scope and the
///         host's limit on the dispatch stay the invoker's: under a handler timeout the callback gets its
///         owner's lifetime linked with that same limit.
///     </para>
///     <para>
///         Nothing is written, and nothing allocated, when the owner's lifetime is already in force — a
///         callback handed straight to an element, which the dispatch already runs for its owner.
///     </para>
/// </remarks>
internal readonly struct CallbackOwnerScope : IDisposable
{
    private readonly Ambient.TokenScope _ambient;
    private readonly IDisposable? _dispatch;
    private readonly CancellationTokenSource? _linked;

    private CallbackOwnerScope(Ambient.TokenScope ambient, IDisposable? dispatch, CancellationTokenSource? linked)
    {
        _ambient = ambient;
        _dispatch = dispatch;
        _linked = linked;
    }

    public static CallbackOwnerScope Enter(Component owner)
    {
        var host = DispatchEventTokenScope.Host;
        if (!host.CanBeCanceled)
        {
            return new CallbackOwnerScope(Ambient.Enter(owner.CallbackLifetime), null, null);
        }

        if (ReferenceEquals(DispatchEventTokenScope.Owner, owner))
        {
            return new CallbackOwnerScope(Ambient.Enter(DispatchEventTokenScope.Current), null, null);
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.CallbackLifetime, host);
        var dispatch = DispatchEventTokenScope.Push(owner, host, linked.Token);
        return new CallbackOwnerScope(Ambient.Enter(linked.Token), dispatch, linked);
    }

    public void Dispose()
    {
        _ambient.Dispose();
        _dispatch?.Dispose();
        _linked?.Dispose();
    }
}
