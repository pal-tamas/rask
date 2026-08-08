using Rask.Core;

namespace Rask.Native.Components;

/// <summary>
///     A tappable button inside a native bar — projected to a <c>UIBarButtonItem</c> (iOS) / a toolbar action
///     (Android). The <see cref="OnClick" /> delegate runs on the render thread and re-renders the owner, like
///     any Rask callback.
/// </summary>
public sealed partial class NativeBarButton : NativeBarItem
{
    /// <summary>The button's icon. Required — every bar button shows an icon.</summary>
    public required NativeIcon Icon { get; set; }

    /// <summary>Invoked when the button is tapped. Optional (a button may be display-only).</summary>
    public Carrier<Action>? OnClick { get; set; }

    /// <summary>An optional accessibility label / title for the button.</summary>
    public new string? Title { get; set; }
}
