using Microsoft.AspNetCore.Components;

namespace Rask.Blazor;

/// <summary>
///     The <see cref="NavigationManager" /> a hosted Blazor component resolves.
/// </summary>
/// <remarks>
///     <para>
///         Registering one is not optional. Most component libraries inject
///         <see cref="NavigationManager" /> somewhere — a link, a tab strip, a breadcrumb — and
///         Blazor's own base class throws from its constructor if the URI was never initialised, so
///         without this the headline case (hosting MudBlazor or Radzen) fails on the first render
///         with an exception that names none of this.
///     </para>
///     <para>
///         Initialised to the application's own base URI. Navigation raises Blazor's
///         <c>LocationChanged</c> so a hosted component that listens still sees it; routing the
///         browser is Rask's own <c>Navigator</c>'s job, and a statically rendered island has no way
///         to reach it from a render.
///     </para>
/// </remarks>
internal sealed class RaskNavigation : NavigationManager
{
    public RaskNavigation(string baseUri, string uri) => Initialize(baseUri, uri);

    /// <inheritdoc />
    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        // Uri has to move before the notification, or a listener reading it sees the old location.
        Uri = ToAbsoluteUri(uri).ToString();
        NotifyLocationChanged(isInterceptedLink: false);
    }
}
