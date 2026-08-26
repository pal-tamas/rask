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
///         This is the <b>imperative</b> path, registered by the in-process <b>WASM</b> host. The default
///         <see cref="Share" /> backing is the Web Share API
///         (<c>navigator.share</c>), which requires a secure context and <em>transient</em> user activation —
///         preserved only when the interop call runs inside the click's own call stack, which the Server's
///         WebSocket round-trip loses. That's why it's an in-process API and lives in <c>Rask.Client</c>.
///     </para>
///     <para>
///         For a <b>declarative</b> share that also works on the Server host, use the all-host, headless
///         <c>Shareable</c> component in <c>Rask.Core</c> — it fires <c>navigator.share</c> client-side inside
///         the click gesture (no round-trip, no activation loss). Use <see cref="IShare" /> when you need to
///         share from code (after an <c>await</c>, a timer, …) on an in-process host.
///     </para>
///     <para>
///         An app can replace the default by registering its own <see cref="IShare" /> before the host's —
///         the registration is a <c>TryAdd</c> fallback.
///     </para>
/// </remarks>
public interface IShare
{
    /// <summary>
    ///     Opens the platform share sheet for <paramref name="data" /> (<c>navigator.share</c>). With the
    ///     default Web Share backing this must be called from a user-gesture handler.
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
