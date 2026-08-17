using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Rask.Core.Browser;

namespace Rask.Core.Components;

/// <summary>
///     Headless <b>gesture bridge</b> — hands your own markup a <c>data-rask-gesture</c> attribute so the
///     element's click runs an activation-gated browser API <b>inside the click's own gesture</b>. That's the
///     one thing a Server round-trip can't do (the transient user activation is gone by the time C# runs), so
///     APIs like fullscreen, the eyedropper, or picture-in-picture — normally WASM-only — become reachable
///     declaratively on the <b>Server</b> host too, the same way <c>Shareable</c> makes sharing work
///     everywhere. Spread the bundle onto any element via its <c>Data</c> prop:
///     <code>
///     GestureTrigger(Capability: "fullscreen.request",
///         trigger => Button(Type: "button", Data: trigger)["Go fullscreen"])
///     </code>
///     For the common capabilities, prefer the typed wrappers (<see cref="FullscreenTrigger" />,
///     <see cref="EyeDropperTrigger" />, <see cref="ScreenOrientationTrigger" />,
///     <see cref="PictureInPictureTrigger" />, <see cref="InstallTrigger" />, <see cref="MediaCaptureTrigger" />).
///     For a <b>code-driven</b> call on the in-process WASM host, inject the matching service
///     (<c>IFullscreen</c>, <c>IEyeDropper</c>, …) instead.
/// </summary>
public sealed class GestureTrigger : Component
{
    /// <summary>The capability to run in the gesture — e.g. <c>"fullscreen.request"</c>, <c>"eyedropper.open"</c>.</summary>
    public required string Capability { get; set; }

    /// <summary>
    ///     Optional callback for capabilities that return a value (the eyedropper's hex, the install outcome).
    ///     When set, the client posts the result back to it; leave <c>null</c> for fire-and-forget capabilities.
    /// </summary>
    public Func<string?, Task>? OnResult { get; set; }

    /// <summary>Renders your trigger element, given the attribute bundle to apply via its <c>Data</c> prop.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr(Capability, OnResult));
}

/// <summary>Present an element/page fullscreen from a click gesture (works on Server, unlike the imperative <c>IFullscreen</c>).</summary>
public sealed class FullscreenTrigger : Component
{
    /// <summary>Optional element to present fullscreen; when <c>null</c>, the whole page goes fullscreen.</summary>
    public ElementRef? For { get; set; }

    /// <summary>Renders your trigger element; its click requests fullscreen for the page (or <see cref="For" />).</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("fullscreen.request", null, el: For?.Id));
}

/// <summary>Open the eyedropper from a click gesture and receive the picked colour (hex, or <c>null</c> if cancelled).</summary>
public sealed class EyeDropperTrigger : Component
{
    /// <summary>Invoked with the picked colour as <c>#rrggbb</c>, or <c>null</c> when the user cancels.</summary>
    public Func<string?, Task>? OnColor { get; set; }

    /// <summary>Renders your trigger element; its click opens the eyedropper.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("eyedropper.open", OnColor));
}

/// <summary>
///     Lock the screen orientation from a click gesture (works on Server, unlike the imperative
///     <c>IScreenOrientation</c>). The browser's <c>screen.orientation.lock</c> only resolves while the page is
///     fullscreen and on a device that honours it, so pair this with a <see cref="FullscreenTrigger" /> (or
///     app-controlled fullscreen); off-fullscreen or on desktop the lock is a silent no-op.
/// </summary>
public sealed class ScreenOrientationTrigger : Component
{
    /// <summary>The orientation to lock to — e.g. <c>"landscape"</c>, <c>"portrait"</c>, <c>"landscape-primary"</c>.</summary>
    public required string Orientation { get; set; }

    /// <summary>Renders your trigger element; its click locks the orientation (a no-op unless the page is fullscreen).</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("orientation.lock", null, arg: Orientation));
}

/// <summary>
///     Put a <c>&lt;video&gt;</c> into picture-in-picture from a click gesture (works on Server, unlike the
///     imperative <c>IPictureInPicture</c>). Point <see cref="For" /> at the video's <see cref="ElementRef" />.
/// </summary>
public sealed class PictureInPictureTrigger : Component
{
    /// <summary>The <c>&lt;video&gt;</c> element to present in the miniplayer.</summary>
    public required ElementRef For { get; set; }

    /// <summary>Renders your trigger element; its click opens the picture-in-picture miniplayer for <see cref="For" />.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("pip.request", null, el: For.Id));
}

/// <summary>
///     Show the PWA install prompt from a click gesture (works on Server, unlike the imperative
///     <c>IInstallPrompt</c>). <see cref="OnOutcome" /> receives <c>"accepted"</c>, <c>"dismissed"</c>, or
///     <c>"unavailable"</c> — the last when the app isn't installable (it needs a web manifest + service worker
///     over HTTPS; on Server that means <c>AddRaskPwa</c>).
/// </summary>
public sealed class InstallTrigger : Component
{
    /// <summary>Invoked with the install outcome: <c>"accepted"</c>, <c>"dismissed"</c>, or <c>"unavailable"</c>.</summary>
    public Func<string?, Task>? OnOutcome { get; set; }

    /// <summary>Renders your trigger element; its click shows the browser's install prompt.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("install.prompt", OnOutcome));
}

/// <summary>
///     Start a camera/microphone stream from a click gesture and attach it to a <c>&lt;video&gt;</c> (works on
///     Server, unlike the imperative <c>IMediaDevices</c>). Needs a secure (HTTPS) context; the stream stays
///     attached to <see cref="For" /> (muted, autoplaying) until the page navigates away.
///     <see cref="OnResult" /> receives <c>"granted"</c> once the stream starts, or <c>"denied"</c> if the user
///     refuses.
/// </summary>
public sealed class MediaCaptureTrigger : Component
{
    /// <summary>The <c>&lt;video&gt;</c> element the captured stream is attached to.</summary>
    public required ElementRef For { get; set; }

    /// <summary>Capture the microphone. Defaults to <c>false</c>.</summary>
    public bool Audio { get; set; } = false;

    /// <summary>Capture the camera. Defaults to <c>true</c>.</summary>
    public bool Video { get; set; } = true;

    /// <summary>Optional camera facing mode — <c>"user"</c> (front) or <c>"environment"</c> (rear).</summary>
    public string? FacingMode { get; set; }

    /// <summary>Invoked with <c>"granted"</c> when the stream starts, or <c>"denied"</c> if the user refuses.</summary>
    public Func<string?, Task>? OnResult { get; set; }

    /// <summary>
    ///     Invoked with the started stream's <see cref="MediaStreamId" />, so the stream stays reachable
    ///     from C# after the gesture — stop it with <see cref="IMediaStreams.StopAsync" />, re-attach it to
    ///     another <c>&lt;video&gt;</c>, or send it to a peer with <c>IPeerConnection.AddStreamAsync</c>.
    ///     Not invoked when the user refuses. This is the only way a <b>Server</b>-hosted app can hold on to
    ///     a captured stream.
    /// </summary>
    public Func<MediaStreamId, Task>? OnStream { get; set; }

    /// <summary>Renders your trigger element; its click starts the capture and attaches it to <see cref="For" />.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render()
    {
        var constraints = JsonSerializer.Serialize(
            new GestureMediaConstraints(Video, Audio, FacingMode),
            RaskBrowserJsonContext.Default.GestureMediaConstraints);
        // Stay fire-and-forget when the app wants no result: passing a non-null sink would register a
        // callback id on every render for nobody to consume.
        var sink = OnResult is null && OnStream is null ? (Func<string?, Task>?)null : Dispatch;
        return Template!(GestureBridge.Attr("media.start", sink, arg: constraints, el: For.Id));
    }

    // The capability resolves the stream's id, or "denied". The bridge posts exactly one result per click,
    // so both callbacks are fed from that one value here rather than costing a second round trip — and
    // OnResult keeps the "granted"/"denied" vocabulary it always had.
    private async Task Dispatch(string? result)
    {
        var started = int.TryParse(result, out var streamId);

        if (started && OnStream is not null)
        {
            await OnStream(new MediaStreamId(streamId));
        }

        if (OnResult is not null)
        {
            await OnResult(started ? "granted" : "denied");
        }
    }
}

// Shared payload for the data-rask-gesture attribute: the capability, plus — when a result is expected —
// the id the client posts the result back under (RaskGestureResult); an optional per-capability argument
// (an orientation type, JSON media constraints); and an optional target element ref-id (the <video> for
// picture-in-picture / media capture). Serialized with the trim-safe source-gen context (Web defaults →
// { "cap": …, "rid": … }); arg/el are omitted when null so the common two-field form stays compact.
internal sealed record GesturePayload(
    string Cap,
    int? Rid,
    [property: JsonPropertyName("arg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Arg = null,
    [property: JsonPropertyName("el"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? El = null);

// Constraints for MediaCaptureTrigger, serialized into the gesture payload's `arg` and JSON.parse'd by the
// client's __raskMedia.getUserMedia (Web defaults → { "video": …, "audio": …, "facingMode": … }). Field
// order mirrors the public Rask.Wasm.Browser.MediaConstraints so the two can't be confused positionally.
internal sealed record GestureMediaConstraints(
    bool Video,
    bool Audio,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FacingMode);

internal static class GestureBridge
{
    // Roots GestureResultInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNet dispatcher (reflection), so without this the Result method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(GestureResultInterop))]
    public static IReadOnlyDictionary<string, string?> Attr(
        string capability, Func<string?, Task>? onResult, string? arg = null, string? el = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(capability);
        var rid = onResult is null ? (int?)null : GestureResultInterop.Register(onResult);
        var json = JsonSerializer.Serialize(
            new GesturePayload(capability, rid, arg, el), RaskBrowserJsonContext.Default.GesturePayload);
        return new Dictionary<string, string?>(StringComparer.Ordinal) { ["rask-gesture"] = json };
    }
}

/// <summary>
///     Infrastructure for the gesture bridge — routes a result the client posts after running a gesture
///     capability back to the right C# callback by id. <b>Not for application use;</b> invoked only by the
///     framework client via <c>window.DotNet.invokeMethodAsync("Rask.Core", "RaskGestureResult", …)</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GestureResultInterop
{
    // A gesture result handler is one-shot: it's removed when its result posts back (Result). But a trigger
    // that re-renders — or is never clicked — leaves its superseded rid orphaned, since the DOM only ever
    // carries the latest. Cap the live map by evicting the entry this many registrations back: by then its
    // rid is long gone from every client's DOM, so it can never fire. High enough that a genuinely-pending
    // handler is never evicted before its click, yet it bounds the static map instead of leaking per-render.
    private const int Capacity = 65536;

    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<string?, Task>> Handlers = new();

    internal static int Register(Func<string?, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        Handlers.TryRemove(id - Capacity, out _);
        return id;
    }

    /// <summary>Infrastructure. Invoked by the client with a gesture's result (one-shot); do not call.</summary>
    [JSInvokable("RaskGestureResult")]
    public static Task Result(int id, string? value) =>
        Handlers.TryRemove(id, out var handler) ? handler(value) : Task.CompletedTask;
}
