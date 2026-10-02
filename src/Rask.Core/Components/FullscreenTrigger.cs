namespace Rask.Core.Components;

/// <summary>Present an element/page fullscreen from a click gesture (works on Server, unlike an imperative <c>RequestFullscreen()</c>).</summary>
[RaskChainGroup(typeof(global::Rask.Trigger))]
public sealed class FullscreenTrigger : Component
{
    /// <summary>Optional element to present fullscreen; when <c>null</c>, the whole page goes fullscreen.</summary>
    public ElementRef? For { get; set; }

    /// <summary>Renders your trigger element; its click requests fullscreen for the page (or <see cref="For" />).</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("fullscreen.request", default, el: For?.Id));
}
