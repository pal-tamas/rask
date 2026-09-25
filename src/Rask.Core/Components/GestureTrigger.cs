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
[RaskChainGroup(typeof(global::Rask.Trigger))]
public sealed class GestureTrigger : Component
{
    /// <summary>The capability to run in the gesture — e.g. <c>"fullscreen.request"</c>, <c>"eyedropper.open"</c>.</summary>
    public required string Capability { get; set; }

    /// <summary>
    ///     Optional callback for capabilities that return a value (the eyedropper's hex, the install outcome).
    ///     When set, the client posts the result back to it; leave <c>null</c> for fire-and-forget capabilities.
    /// </summary>
    public Callback<string?> OnResult { get; set; }

    /// <summary>Renders your trigger element, given the attribute bundle to apply via its <c>Data</c> prop.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr(Capability, OnResult));
}
