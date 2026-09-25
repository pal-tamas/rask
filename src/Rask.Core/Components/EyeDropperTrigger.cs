namespace Rask.Core.Components;

/// <summary>Open the eyedropper from a click gesture and receive the picked colour (hex, or <c>null</c> if cancelled).</summary>
[RaskChainGroup(typeof(global::Rask.Trigger))]
public sealed class EyeDropperTrigger : Component
{
    /// <summary>Invoked with the picked colour as <c>#rrggbb</c>, or <c>null</c> when the user cancels.</summary>
    public Callback<string?> OnColor { get; set; }

    /// <summary>Renders your trigger element; its click opens the eyedropper.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("eyedropper.open", OnColor));
}
