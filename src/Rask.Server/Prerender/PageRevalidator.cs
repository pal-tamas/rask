using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core.Authorization;
using Rask.Core.Diagnostics;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Server.Diagnostics;

namespace Rask.Server.Prerender;

/// <summary>
///     Renders public pages in the background, for nobody, and hands the result to the <see cref="PageCache" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a background service rather than the request.</b> A copy has to be a render no visitor
///         influenced, and a request is exactly what a visitor influences: its principal, its cookies, its
///         query, whatever a component reads off the context. Work queued here runs on a flow that began when
///         the host started, so there is no request context in it to leak.
///     </para>
///     <para>
///         Live traffic comes first. A render is skipped while the host is draining or at its session cap —
///         the page keeps serving whatever it had, and is asked for again on a later request — and at most a
///         few run at once.
///     </para>
///     <para>
///         <b>Nothing here may throw out of the service.</b> A background service that faults stops the whole
///         host by default, and no failure of the cache is worth that: every path that can fail keeps the copy
///         or the plan it had, says so once, and carries on.
///     </para>
/// </remarks>
internal sealed class PageRevalidator(
    PageCache cache,
    LiveSessionStore store,
    RaskServerLimits limits,
    IServiceScopeFactory scopes,
    TimeProvider time,
    RaskMetrics? metrics = null) : BackgroundService
{
    private const string Category = "Rask.Prerender";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!cache.Enabled)
        {
            return;
        }

        RaskRootSelector selector;
        try
        {
            selector = await cache.Attached.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Off the startup path: planning may ask an IPrerenderPaths that reads a database.
        await Task.Yield();
        RefreshPlan(selector);

        var workers = new Task[limits.RevalidationConcurrency + 1];
        for (var i = 0; i < limits.RevalidationConcurrency; i++)
        {
            workers[i] = RenderRequestedAsync(selector, stoppingToken);
        }

        workers[^1] = RefreshPlanPeriodicallyAsync(selector, stoppingToken);

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    ///     Renders <paramref name="key" /> for nobody and applies the verdict to its stored copy.
    /// </summary>
    /// <returns>
    ///     The outcome, or <c>null</c> when the render was skipped to leave room for live traffic.
    /// </returns>
    internal async Task<string?> RevalidateAsync(
        RaskRootSelector selector,
        PageCacheKey key,
        CancellationToken cancellationToken)
    {
        if (store.IsDraining || store.AtCapacity)
        {
            return null;
        }

        if (!RouteResolver.TryResolve(selector.HostRoutes, key.Path, out var chain, out var isNotFound) || isNotFound)
        {
            return Refuse(key, "no page answers that path any more");
        }

        // The request path already refuses these, so this is the plan changing underneath: a route that
        // gained an [Authorize] since the copy was made.
        if (RouteAuthorizationGuard.RequiresAuthorization(chain))
        {
            return Refuse(key, "it requires authorization");
        }

        // The scoped bundles as they stand before the render. A page's <link> names the bundle as it is when
        // its document is serialized, so the hash stored with the copy has to be that one — and if anything
        // registers assets while this renders, there is no telling which the document names.
        var cssBefore = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        var jsBefore = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);

        var session = store.CreateDetached(selector.FactoryFor(key.Path));
        try
        {
            var render = await PageRender.RenderAsync(
                    session,
                    new PageRenderInput(
                        key.Path,
                        QueryCollection.Empty,
                        // A fresh principal per render: ClaimsPrincipal is mutable, and a page that added an
                        // identity to a shared one would sign in every render after it.
                        new ClaimsPrincipal(new ClaimsIdentity()),
                        CultureOf(key),
                        chain,
                        NotFoundPage: null),
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);

            var css = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
            var js = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
            if (!string.Equals(css, cssBefore, StringComparison.Ordinal)
                || !string.Equals(js, jsBefore, StringComparison.Ordinal))
            {
                // Styles registered while this page rendered — its own components on their first render, or
                // another render running beside it. Stored now, the copy could name a bundle that no longer
                // resolves and still be judged current. Not stored; the next request for the page asks again,
                // and by then the set has settled.
                metrics?.PrerenderRevalidated("unsettled");
                return "unsettled";
            }

            var document = PageDocument.Baked(render, LiveOptions.PathBase);
            var verdict = PageStorePolicy.Decide(
                render,
                document,
                document is null ? 0 : Encoding.UTF8.GetByteCount(document),
                limits.PageCacheMaxEntryBytes);

            var outcome = cache.Apply(key, verdict, document, render.ReadsUser, css, js);

            Report(key, verdict, outcome);
            metrics?.PrerenderRevalidated(outcome);
            return outcome;
        }
        finally
        {
            await store.DiscardAsync(session).ConfigureAwait(false);
        }
    }

    private async Task RenderRequestedAsync(RaskRootSelector selector, CancellationToken stoppingToken)
    {
        await foreach (var key in cache.Requests.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RevalidateAsync(selector, key, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // Something below the page threw outside its error boundary — building the tree, or the render
                // machinery itself. Treated like a render that threw: a copy it had keeps serving.
                var outcome = cache.Apply(
                    key,
                    new PageStoreVerdict(PageStoreDecision.KeepStale, "rendering it threw"),
                    document: null,
                    readsUser: false,
                    cssBundle: string.Empty,
                    jsBundle: string.Empty);
                metrics?.PrerenderRevalidated(outcome);
                RaskDiagnostics.ReportOnce(
                    "prerender:threw:" + key.Path + ":" + error.GetType().FullName,
                    RaskLogLevel.Warning,
                    Category,
                    () => $"Rendering {key.Path} for the page cache threw; it is served live until a render "
                          + "succeeds.",
                    error);
            }
            finally
            {
                cache.Completed(key);
            }
        }
    }

    private async Task RefreshPlanPeriodicallyAsync(RaskRootSelector selector, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(limits.PlannedPathsRefresh, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            RefreshPlan(selector);
        }
    }

    private void RefreshPlan(RaskRootSelector selector)
    {
        try
        {
            if (cache.Planned.Refresh(selector.HostRoutes, scopes, limits.PageCacheMaxPaths))
            {
                cache.PruneUnplanned();
            }
        }
        catch (Exception error)
        {
            // Reaching here means something outside the app's sources failed — the route table itself. The
            // plan stays as it was, and the loop that called this keeps running: a plan that cannot be
            // refreshed is a reason to keep the old one, never to stop the host.
            RaskDiagnostics.ReportOnce(
                "prerender:plan-threw:" + error.GetType().FullName,
                RaskLogLevel.Warning,
                Category,
                () => "Planning the page cache's paths threw, so it kept the paths it had.",
                error);
        }
    }

    private string Refuse(PageCacheKey key, string reason)
    {
        var verdict = new PageStoreVerdict(PageStoreDecision.Evict, reason);
        var outcome = cache.Apply(key, verdict, null, readsUser: false, string.Empty, string.Empty);
        Report(key, verdict, outcome);
        metrics?.PrerenderRevalidated(outcome);
        return outcome;
    }

    // Once per page and reason: a page that is not cacheable stays that way, and saying so on every refresh
    // would bury the one line that explains it.
    private static void Report(PageCacheKey key, PageStoreVerdict verdict, string outcome)
    {
        // A render the policy would have stored, turned away by the cache itself — its budget, or a plan that
        // moved underneath. The cache says why where it decides, so there is nothing to add here.
        if (verdict.Reason.Length == 0)
        {
            return;
        }

        switch (outcome)
        {
            case "stored" or "unchanged":
                return;

            case "kept":
                RaskDiagnostics.ReportOnce(
                    "prerender:kept:" + key.Path + ":" + verdict.Reason,
                    RaskLogLevel.Warning,
                    Category,
                    () => $"Rendering {key.Path} again did not produce a page to store — {verdict.Reason}. The "
                          + "copy it had is still being served.");
                return;

            default:
                RaskDiagnostics.ReportOnce(
                    "prerender:live:" + key.Path + ":" + verdict.Reason,
                    RaskLogLevel.Information,
                    Category,
                    () => $"{key.Path} is served live, not cached — {verdict.Reason}.");
                return;
        }
    }

    private static CultureNegotiation? CultureOf(PageCacheKey key) =>
        key.Culture.Length == 0
            ? null
            : new CultureNegotiation(
                CultureInfo.GetCultureInfo(key.Culture),
                CultureInfo.GetCultureInfo(key.UICulture),
                CultureSource.Default);
}
