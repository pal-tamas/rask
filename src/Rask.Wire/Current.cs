namespace Rask;

/// <summary>
///     The work in flight — its services, who it is for and when it should stop — read from anywhere, with nothing
///     injected: <c>Current.Service&lt;T&gt;()</c> and <c>Current.Cancellation</c> here, and with Rask.Data referenced <c>Current.UserId</c>, <c>Current.Principal</c>
///     and <c>Current.Tenant</c> too.
/// </summary>
/// <remarks>
///     A handler, a lifecycle hook and a static call like <c>Cache.Remember(…)</c> take no token: they run inside a
///     handler dispatch, a render, a request or a job, and stop with it. Code that is not Rask — an
///     <c>HttpClient</c>, a payment SDK — only stops if it is handed a token, and this is the one to hand it:
///     <code>
///     public async Task Handle(SyncStock job) =&gt;
///         await http.GetAsync(job.Url, Current.Cancellation);
///     </code>
/// </remarks>
public static class Current
{
    /// <summary>
    ///     The cancellation of the work in flight: cancelled when the component unmounts, the request is aborted,
    ///     the job's host stops or the handler times out. <see cref="CancellationToken.None" /> outside any work.
    /// </summary>
    public static CancellationToken Cancellation => Ambient.CancellationToken;

    /// <summary>
    ///     The <typeparamref name="T" /> of the work in flight, as its constructor would have been handed it — for a
    ///     component, whose constructor is the session's, to reach the event's own <c>DbContext</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In a live event — a click, a submit, a navigation, a render the server pushes and the lifecycle hooks
    ///         they run — a scoped service is the event's own: the same instance for the whole event, awaits
    ///         included, and disposed when the event ends. Keep it in a local, never a field: the next event finds
    ///         it disposed. The session's own services (its route, navigator, <c>IJSRuntime</c>, user, toaster,
    ///         language, query cache, tenant) are the session's, as they are to a service the event makes.
    ///         <code>
    ///         Button.OnClick(async () =&gt;
    ///         {
    ///             var db = Current.Service&lt;ShopDb&gt;();
    ///             db.Orders.Add(order);
    ///             await db.SaveChangesAsync();
    ///         })
    ///         </code>
    ///     </para>
    ///     <para>
    ///         In an HTTP request it is the request's; in a background job, the job's scope.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     No work is in flight — a constructor, a hosted service's own loop — or <typeparamref name="T" /> is not
    ///     registered.
    /// </exception>
    public static T Service<T>()
        where T : notnull =>
        Ambient.Require(
                $"Current.Service<{typeof(T).Name}>()",
                $"Inject {typeof(T).Name} in the constructor there, or open a scope (IServiceScopeFactory.CreateAsyncScope()) and resolve it from that.")
            .GetService(typeof(T)) is T service
            ? service
            : throw new InvalidOperationException($"No service for type '{typeof(T)}' has been registered.");
}
