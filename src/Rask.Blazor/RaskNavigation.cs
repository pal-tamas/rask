using Microsoft.AspNetCore.Components;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Blazor;

/// <summary>
///     The <see cref="NavigationManager" /> a hosted Blazor component resolves.
/// </summary>
/// <remarks>
///     <para>
///         A view onto Rask's own routing rather than a second copy of it. <see cref="NavigationManager.Uri" />
///         is the session's <see cref="RouteState" /> under the app's path base, so a hosted
///         <c>NavLink</c>, breadcrumb or tab strip matches against the page actually on screen;
///         <see cref="NavigationManager.NavigateTo(string, bool)" /> goes through Rask's
///         <see cref="Navigator" />, so the browser moves; and a navigation Rask makes raises Blazor's
///         <c>LocationChanged</c>.
///     </para>
///     <para>
///         Rask's route carries no origin, so the scheme and host come from
///         <see cref="RaskBlazorOptions.BaseUri" />. Everything a component computes relative to the
///         base — which is what active-link matching is — is right whatever that host says.
///     </para>
/// </remarks>
internal sealed class RaskNavigation : NavigationManager, IDisposable
{
    private readonly RouteState? _route;
    private readonly Navigator? _navigator;
    private readonly Func<Action, Task> _onRenderer;

    /// <summary>Opens on the session's current location and follows it from then on.</summary>
    /// <param name="origin">The configured base URI; only its scheme and host are read.</param>
    /// <param name="route">The session's location, or <c>null</c> when nothing routes this render.</param>
    /// <param name="navigator">What moves the browser, or <c>null</c> when nothing routes this render.</param>
    /// <param name="onRenderer">Runs a callback on the hosted component's dispatcher.</param>
    public RaskNavigation(string origin, RouteState? route, Navigator? navigator, Func<Action, Task> onRenderer)
    {
        _route = route;
        _navigator = navigator;
        _onRenderer = onRenderer;

        var baseUri = BaseOf(origin);
        Initialize(baseUri, Current(baseUri));

        if (_route is not null)
        {
            _route.Changed += OnRouteChanged;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_route is not null)
        {
            _route.Changed -= OnRouteChanged;
        }
    }

    /// <inheritdoc />
    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        var target = ToAbsoluteUri(uri).ToString();

        if (_navigator is null || options.ForceLoad || !InApp(target))
        {
            // Nothing of Rask's can take it: no session, a forced reload, or another site. The hosted
            // component still sees where it asked to go, which is all a render without a browser can offer.
            Uri = target;
            NotifyLocationChanged(isInterceptedLink: false);
            return;
        }

        // The route change this makes comes back through OnRouteChanged, which is what moves Uri.
        var relative = "/" + ToBaseRelativePath(target);
        var query = relative.IndexOf('?', StringComparison.Ordinal);
        var url = query < 0 ? new RouteUrl(relative) : new RouteUrl(relative[..query], relative[query..]);

        _navigator.NavigateTo(url, options.ReplaceHistoryEntry);
    }

    private bool InApp(string target) =>
        target.StartsWith(BaseUri, StringComparison.Ordinal)
        || string.Equals(target, BaseUri.TrimEnd('/'), StringComparison.Ordinal);

    // A Rask navigation can come from any thread; a LocationChanged listener calls StateHasChanged, which
    // only the renderer's own dispatcher may do.
    private void OnRouteChanged(object? sender, EventArgs e) => _ = _onRenderer(Sync);

    private void Sync()
    {
        var current = Current(BaseUri);
        if (string.Equals(current, Uri, StringComparison.Ordinal))
        {
            return;
        }

        Uri = current;
        NotifyLocationChanged(isInterceptedLink: false);
    }

    private string Current(string baseUri) =>
        _route is null ? baseUri : baseUri.TrimEnd('/') + Navigator.BuildUrl(_route);

    // NavigationManager.Initialize requires a base ending in '/', and throws an ArgumentException naming
    // neither Rask nor the option when it does not (#948).
    private static string BaseOf(string origin) =>
        new Uri(new Uri(origin), "/").ToString().TrimEnd('/') + LiveOptions.PathBase + "/";
}
