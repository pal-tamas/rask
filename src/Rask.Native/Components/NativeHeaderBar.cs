namespace Rask.Native.Components;

/// <summary>
///     A native top bar — composed at a native page's layout level (a sibling of <see cref="NativeWebView" />),
///     projected to a <c>UINavigationBar</c> (iOS) or <c>MaterialToolbar</c> (Android). Renders no HTML.
/// </summary>
/// <example>
///     <code>Render() => [NativeHeaderBar(Title: "Dashboard", Leading: NativeBackButton(),
///                         Trailing: [NativeBarButton(Icon: NativeIcon.Add, OnClick: OnAdd)]),
///                     NativeWebView()[/* shell */]];</code>
/// </example>
public sealed class NativeHeaderBar : NativeComponent
{
    /// <summary>The bar's title, shown centred (iOS) / leading (Android) per platform convention.</summary>
    public string? Title { get; set; }

    /// <summary>An optional leading item (e.g. a <see cref="NativeBackButton" />), shown at the start of the bar.</summary>
    public NativeBarItem? Leading { get; set; }

    /// <summary>Optional trailing items, shown at the end of the bar. Nullable with no initializer so it stays a factory parameter.</summary>
    public IReadOnlyList<NativeBarItem>? Trailing { get; set; }

    /// <summary>The bar's background color. Leave <c>null</c> to inherit the registered <c>NativeTheme</c>, else the platform default.</summary>
    public NativeColor? Background { get; set; }

    /// <summary>The tint applied to the leading/trailing bar buttons. <c>null</c> ⇒ theme, else platform default.</summary>
    public NativeColor? Tint { get; set; }

    /// <summary>The title text color. <c>null</c> ⇒ theme, else platform default.</summary>
    public NativeColor? TitleColor { get; set; }
}
