namespace Rask;

/// <summary>
///     The work in flight — who it is for and when it should stop — read from anywhere, with nothing injected:
///     <c>Current.Cancellation</c> here, and with Rask.Data referenced <c>Current.UserId</c>, <c>Current.Principal</c>
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
}
