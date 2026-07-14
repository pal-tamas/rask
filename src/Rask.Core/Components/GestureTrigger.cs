using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Rask.Core.Browser;
using Microsoft.JSInterop;

namespace Rask.Core.Components;

/// <summary>
///     Headless <b>gesture bridge</b> — hands your own markup a <c>data-rask-gesture</c> attribute so the
///     element's click runs an activation-gated browser API <b>inside the click's own gesture</b>. That's the
///     one thing a Server round-trip can't do (the transient user activation is gone by the time C# runs), so
///     APIs like fullscreen, the eyedropper, or picture-in-picture — normally WASM-only — become reachable
///     declaratively on the <b>Server</b> host too, the same way <see cref="Shareable" /> makes sharing work
///     everywhere. Spread the bundle onto any element via its <c>Data</c> prop:
///     <code>
///     GestureTrigger(Capability: "fullscreen.request",
///         trigger => Button(Type: "button", Data: trigger)["Go fullscreen"])
///     </code>
///     For the common capabilities, prefer the typed wrappers (<see cref="FullscreenTrigger" />,
///     <see cref="EyeDropperTrigger" />). For a <b>code-driven</b> call on the in-process WASM host, inject the
///     matching service (<c>IFullscreen</c>, <c>IEyeDropper</c>, …) instead.
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
    protected override Component Render() => Template(GestureBridge.Attr(Capability, OnResult));
}

/// <summary>Present an element/page fullscreen from a click gesture (works on Server, unlike the imperative <c>IFullscreen</c>).</summary>
public sealed class FullscreenTrigger : Component
{
    /// <summary>Renders your trigger element; its click requests fullscreen for the whole page.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template(GestureBridge.Attr("fullscreen.request", null));
}

/// <summary>Open the eyedropper from a click gesture and receive the picked colour (hex, or <c>null</c> if cancelled).</summary>
public sealed class EyeDropperTrigger : Component
{
    /// <summary>Invoked with the picked colour as <c>#rrggbb</c>, or <c>null</c> when the user cancels.</summary>
    public Func<string?, Task>? OnColor { get; set; }

    /// <summary>Renders your trigger element; its click opens the eyedropper.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template(GestureBridge.Attr("eyedropper.open", OnColor));
}

// Shared payload for the data-rask-gesture attribute: the capability plus, when a result is expected, the
// id the client posts the result back under (RaskGestureResult). Serialized with the trim-safe source-gen
// context (Web defaults → { "cap": …, "rid": … }).
internal sealed record GesturePayload(string Cap, int? Rid);

internal static class GestureBridge
{
    // Roots GestureResultInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNet dispatcher (reflection), so without this the Result method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(GestureResultInterop))]
    public static IReadOnlyDictionary<string, string?> Attr(string capability, Func<string?, Task>? onResult)
    {
        ArgumentException.ThrowIfNullOrEmpty(capability);
        var rid = onResult is null ? (int?)null : GestureResultInterop.Register(onResult);
        var json = JsonSerializer.Serialize(
            new GesturePayload(capability, rid), RaskBrowserJsonContext.Default.GesturePayload);
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
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<string?, Task>> Handlers = new();

    internal static int Register(Func<string?, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    /// <summary>Infrastructure. Invoked by the client with a gesture's result (one-shot); do not call.</summary>
    [JSInvokable("RaskGestureResult")]
    public static Task Result(int id, string? value) =>
        Handlers.TryRemove(id, out var handler) ? handler(value) : Task.CompletedTask;
}
