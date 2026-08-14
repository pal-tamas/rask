namespace Rask.Native.Components;

/// <summary>
///     A native contextual bottom toolbar — composed at a native page's layout level (a sibling of
///     <see cref="NativeWebView" />), projected to a <c>UIToolbar</c> (iOS) / a bottom <c>MaterialToolbar</c>
///     (Android). Use for contextual actions rather than primary navigation (that is <see cref="NativeTabBar" />).
/// </summary>
public sealed partial class NativeToolbar : NativeComponent
{
    /// <summary>The toolbar's action items, in order. Nullable with no initializer so it stays a factory parameter.</summary>
    public IReadOnlyList<NativeBarItem>? Items { get; set; }

    /// <summary>The toolbar's background color. Leave <c>null</c> to inherit the registered <c>NativeTheme</c>, else the platform default.</summary>
    public NativeColor? Background { get; set; }

    /// <summary>The tint applied to the toolbar action items. <c>null</c> ⇒ theme, else platform default.</summary>
    public NativeColor? Tint { get; set; }
}
