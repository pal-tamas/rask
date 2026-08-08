namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     Signals that the database has been restored and its schema created.
/// </summary>
/// <remarks>
///     Hosted services start at the <em>end</em> of boot on the browser host — after the first render, so
///     a background service can safely call <c>StateHasChanged</c> against a mounted tree. The
///     consequence is that a component's <c>OnMountAsync</c> runs <em>before</em> the schema exists, and
///     querying there would fail with "no such table" on the very first paint.
///     <para>
///         Registration order gets the services started in the right sequence, but it does not make one
///         <em>ready</em> before another — so readiness is stated explicitly, exactly as
///         <c>docs/lifecycle.md</c> recommends. Awaiting this is cheap: it is already completed on every
///         render after the first.
///     </para>
/// </remarks>
public sealed class DatabaseReady
{
    private readonly TaskCompletionSource _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the database is usable.</summary>
    public Task Ready => _source.Task;

    public void Signal() => _source.TrySetResult();
}
