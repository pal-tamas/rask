namespace Rask.Core.Components;

/// <summary>
///     Lock the screen orientation from a click gesture (works on Server, unlike the imperative
///     <c>IScreenOrientation</c>). The browser's <c>screen.orientation.lock</c> only resolves while the page is
///     fullscreen and on a device that honours it, so pair this with a <see cref="FullscreenTrigger" /> (or
///     app-controlled fullscreen); off-fullscreen or on desktop the lock is a silent no-op.
/// </summary>
[RaskChainGroup(typeof(global::Rask.Trigger))]
public sealed class ScreenOrientationTrigger : Component
{
    /// <summary>The orientation to lock to — e.g. <c>"landscape"</c>, <c>"portrait"</c>, <c>"landscape-primary"</c>.</summary>
    public required string Orientation { get; set; }

    /// <summary>Renders your trigger element; its click locks the orientation (a no-op unless the page is fullscreen).</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("orientation.lock", null, arg: Orientation));
}
