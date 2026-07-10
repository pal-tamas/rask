using Microsoft.JSInterop;
using Rask.Core.Browser;

namespace Rask.Client.Browser;

/// <summary>
///     Typed access to the OS share sheet — hand content to the platform share UI from <b>any</b> handler.
///     Inject through a component constructor and call from an event handler or lifecycle hook:
///     <code>
///     if (await share.CanShareAsync())
///         await share.ShareAsync(new ShareData { Title = "Rask", Url = "https://example.com" });
///     </code>
/// </summary>
/// <remarks>
///     <para>
///         This is the <b>imperative</b> path, registered by the <b>WASM</b> and <b>Native</b> hosts (both
///         in-process). The default <see cref="Share" /> backing is the Web Share API
///         (<c>navigator.share</c>), which requires a secure context and <em>transient</em> user activation —
///         preserved only when the interop call runs inside the click's own call stack, which the Server's
///         WebSocket round-trip loses. That's why it's an in-process API and lives in <c>Rask.Client</c>
///         (which <c>Rask.Native</c> can share; it can't reference the browser-targeted <c>Rask.Wasm</c>).
///     </para>
///     <para>
///         For a <b>declarative</b> share that also works on the Server host, use the all-host, headless
///         <c>Shareable</c> component in <c>Rask.Core</c> — it fires <c>navigator.share</c> client-side inside
///         the click gesture (no round-trip, no activation loss), and upgrades to a native backend in the
///         native shell. Use <see cref="IShare" /> when you need to share from code (after an <c>await</c>, a
///         timer, …) on an in-process host.
///     </para>
///     <para>
///         On the <b>Native</b> host a platform head can replace this default with a native backend
///         (iOS <c>UIActivityViewController</c> / Android <c>Intent.ACTION_SEND</c>) by registering its own
///         <see cref="IShare" /> on <c>host.Services</c> before <c>RunLocalAsync</c> — the native path needs
///         no user activation and works even where the WebView lacks <c>navigator.share</c>.
///     </para>
/// </remarks>
public interface IShare
{
    /// <summary>
    ///     Opens the platform share sheet for <paramref name="data" /> (<c>navigator.share</c>). With the
    ///     default Web Share backing this must be called from a user-gesture handler; a native backend has
    ///     no such requirement.
    /// </summary>
    ValueTask ShareAsync(ShareData data);

    /// <summary>
    ///     Returns whether the platform can share <paramref name="data" /> (<c>navigator.canShare</c>), or
    ///     whether sharing is supported at all when <paramref name="data" /> is <c>null</c>. Returns
    ///     <c>false</c> rather than throwing when the API is unavailable.
    /// </summary>
    ValueTask<bool> CanShareAsync(ShareData? data = null);
}

/// <summary>Default <see cref="IShare" />, backed by the Web Share API via the unified <see cref="IJSRuntime" />.</summary>
public sealed class Share(IJSRuntime js) : IShare
{
    /// <inheritdoc />
    public ValueTask ShareAsync(ShareData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return js.InvokeVoidAsync("navigator.share", data);
    }

    /// <inheritdoc />
    public async ValueTask<bool> CanShareAsync(ShareData? data = null)
    {
        try
        {
            // navigator.canShare() (no arg) reports whether sharing is supported; with data it checks
            // that specific payload. An undefined navigator.canShare faults — treat that as "can't".
            return await js.InvokeAsync<bool>("navigator.canShare", data ?? new ShareData());
        }
        catch (JSException)
        {
            return false;
        }
    }
}
