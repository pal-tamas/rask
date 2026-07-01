using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Badging API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Badging_API" />) — set a numeric or
///     dot badge on the <em>installed</em> app's icon (and, on some platforms, the tab). Pairs with
///     <see cref="INotifications" /> / <see cref="IWebPush" /> to surface an unread count without opening
///     a notification. Works on both hosts (the badge targets the installed PWA instance regardless of
///     transport); inject it through a component constructor.
/// </summary>
/// <remarks>
///     Requires a secure context and (on most platforms) an installed PWA — setting a badge in a normal
///     browser tab is a silent no-op. Gate on <see cref="IsSupportedAsync" />; an unsupported browser
///     surfaces as a <see cref="JSException" />, so wrap calls in try/catch.
/// </remarks>
public interface IBadge
{
    /// <summary>Whether the browser supports app badges (<c>"setAppBadge" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Sets the app badge (<c>navigator.setAppBadge</c>). With <paramref name="count" /> set to a
    ///     positive number the badge shows that number; <c>null</c> (or <c>0</c>) shows a plain dot/flag.
    /// </summary>
    ValueTask SetAsync(int? count = null);

    /// <summary>Clears the app badge (<c>navigator.clearAppBadge</c>).</summary>
    ValueTask ClearAsync();
}

/// <summary>
///     Default <see cref="IBadge" />, backed by the unified <see cref="IJSRuntime" />. The optional-argument
///     shape of <c>setAppBadge()</c> (no arg = dot, number = count) and the feature check go through the
///     framework's <c>__raskBadge</c> helper so the C# surface stays a clean nullable <c>int</c>.
/// </summary>
public sealed class Badge(IJSRuntime js) : IBadge
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskBadge.isSupported");

    /// <inheritdoc />
    public ValueTask SetAsync(int? count = null) => js.InvokeVoidAsync("__raskBadge.set", count);

    /// <inheritdoc />
    public ValueTask ClearAsync() => js.InvokeVoidAsync("__raskBadge.clear");
}
