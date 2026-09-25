namespace Rask.Core.Components;

/// <summary>
///     Put a <c>&lt;video&gt;</c> into picture-in-picture from a click gesture (works on Server, unlike the
///     imperative <c>IPictureInPicture</c>). Point <see cref="For" /> at the video's <see cref="ElementRef" />.
/// </summary>
[RaskChainGroup(typeof(global::Rask.Trigger))]
public sealed class PictureInPictureTrigger : Component
{
    /// <summary>The <c>&lt;video&gt;</c> element to present in the miniplayer.</summary>
    public required ElementRef For { get; set; }

    /// <summary>Renders your trigger element; its click opens the picture-in-picture miniplayer for <see cref="For" />.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("pip.request", default, el: For.Id));
}
