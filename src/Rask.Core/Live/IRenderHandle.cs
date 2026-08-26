namespace Rask.Core.Live;

public interface IRenderHandle
{
    Task RequestRenderAsync();

    /// <summary>
    ///     Request a "publish" render — same render walk, but skips re-firing
    ///     <c>OnRendered</c> / <c>OnRenderedAsync</c> on components that have
    ///     already rendered at least once. The framework uses this from
    ///     <c>OnRenderedAsync</c>'s continuation so state mutated post-await
    ///     paints, without re-entering the very lifecycle hook that scheduled the
    ///     render (which would loop). First-time renders still fire their hooks so
    ///     newly-mounted components get their first <c>OnRendered(firstRender:true)</c>.
    ///     Hosts that don't need the distinction can let the default impl forward to
    ///     <see cref="RequestRenderAsync" />.
    /// </summary>
    Task RequestPublishRenderAsync() => RequestRenderAsync();

    internal Task RenderInScopeAsync() => Task.CompletedTask;

    /// <summary>
    ///     Records a development fault to paint <em>over</em> the app, reported by
    ///     <c>RootErrorBoundary</c> during the render walk that follows it.
    /// </summary>
    /// <remarks>
    ///     It goes to the handle rather than staying on the <see cref="LiveRenderContext" /> because the
    ///     context is disposed when the walk ends, and the frame is built afterwards — a session reading
    ///     it from the context would find nothing. The session holds it until it writes the payload.
    ///     <para>
    ///         Defaulted to a no-op so the interface stays non-breaking, and so the non-session
    ///         implementations (the unit-test render handle) simply don't have an overlay.
    ///     </para>
    /// </remarks>
    internal void ReportDevError(DevErrorInfo error)
    {
    }

    /// <summary>
    ///     Record that something in this render needs a live connection to work.
    /// </summary>
    /// <remarks>
    ///     Reported to the handle rather than the <see cref="LiveRenderContext" /> for the same
    ///     reason <see cref="ReportDevError" /> is: the context is disposed when the walk ends and
    ///     the host reads the verdict afterwards, so a value left on the context would be gone by
    ///     the time anyone asked.
    ///     <para>
    ///         Accumulates — never clears — across the waves of one initial render. A handler that
    ///         appeared on the first wave still needs a socket even if its subtree came back from
    ///         the clean-subtree cache on the second, and a verdict computed from the final walk
    ///         alone would quietly drop it.
    ///     </para>
    ///     <para>
    ///         Defaulted to a no-op so the interface stays non-breaking and the unit-test handle
    ///         simply never forms a verdict.
    ///     </para>
    /// </remarks>
    internal void ReportRequiresLiveSession(InteractivityReason reason)
    {
    }

    // The render engine, surfaced to components during render (via LiveRenderContext → Component.HostEngine).
    // Defaulted here so the interface stays non-breaking; concrete sessions override with their own fact.
    internal RenderEngine Engine => RenderEngine.Server;

    // The session's culture, read fresh by LiveRenderContext at the top of every render walk.
    //
    // Pulled rather than pushed, and that is the whole point: LifecycleSyncContext deliberately calls
    // ExecutionContext.SuppressFlow() so a continuation cannot inherit InHandlerScope, and since .NET
    // Core CultureInfo.CurrentCulture lives in an AsyncLocal riding that same ExecutionContext. Anything
    // that had to FLOW to the render thread — the ambient culture, or an AsyncLocal of our own — would
    // be lost exactly there. Asking the session per walk is immune, because nothing has to survive.
    internal System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.CurrentCulture;

    // As above, for the language UI text renders in.
    internal System.Globalization.CultureInfo UICulture => System.Globalization.CultureInfo.CurrentUICulture;

    // Whether the culture above came from a SESSION or is merely the thread's.
    //
    // Load-bearing for <html lang>: without it, a render outside any session (a unit test, a static
    // ToHtml) would report the machine's locale as the document language, turning lang="en" into
    // lang="en-US" on a US machine. The process-wide RaskCulture.IsEnabled flag cannot answer this —
    // it only says whether SOME host in this process configured cultures, which is not a fact about
    // the render in front of you.
    internal bool HasCulture => false;
}
