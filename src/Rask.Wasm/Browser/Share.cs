using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Payload for the Web Share API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Navigator/share" />). At least one
///     field should be set; <c>null</c> fields are omitted from the share.
/// </summary>
public sealed record ShareData
{
    /// <summary>Title of the shared content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    /// <summary>Body text to share.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>URL to share.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }
}

/// <summary>
///     Typed access to the Web Share API — hand content to the OS share sheet. <b>WASM-only:</b>
///     <c>navigator.share()</c> requires <em>transient</em> user activation, which is preserved only when
///     the interop call runs in the click's call stack (WASM, in-process); on the Server transport the
///     WebSocket round-trip loses it, so this service is registered only by the WASM host. Inject it
///     through a component constructor and call from an event handler:
///     <code>
///     if (await share.CanShareAsync())
///         await share.ShareAsync(new ShareData { Title = "Rask", Url = "https://example.com" });
///     </code>
/// </summary>
/// <remarks>
///     Requires a secure context. A cancel or an unsupported browser surfaces as a
///     <see cref="JSException" /> from <see cref="ShareAsync" /> — gate on <see cref="CanShareAsync" />
///     and wrap the call in try/catch.
/// </remarks>
public interface IShare
{
    /// <summary>
    ///     Opens the platform share sheet for <paramref name="data" /> (<c>navigator.share</c>). Must be
    ///     called from a user-gesture handler.
    /// </summary>
    ValueTask ShareAsync(ShareData data);

    /// <summary>
    ///     Returns whether the browser can share <paramref name="data" /> (<c>navigator.canShare</c>), or
    ///     whether sharing is supported at all when <paramref name="data" /> is <c>null</c>. Returns
    ///     <c>false</c> rather than throwing when the API is unavailable.
    /// </summary>
    ValueTask<bool> CanShareAsync(ShareData? data = null);
}

/// <summary>Default <see cref="IShare" />, backed by the unified <see cref="IJSRuntime" />.</summary>
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
