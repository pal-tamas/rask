namespace Rask.Native.Components;

/// <summary>
///     A native bottom tab bar — composed at a native page's layout level (a sibling of
///     <see cref="NativeWebView" />), projected to a <c>UITabBar</c> (iOS) or <c>BottomNavigationView</c>
///     (Android). Primary navigation: tapping a tab routes to its <see cref="NativeTab.To" />; the framework
///     re-renders and updates <see cref="Selected" />.
/// </summary>
/// <example>
///     <code>NativeTabBar(Tabs: [NativeTab(Title: "Home", Icon: NativeIcon.Home, To: Features.Routes.Home()),
///                         NativeTab(Title: "Me",   Icon: NativeIcon.Person, To: Features.Routes.Me())],
///                  Selected: 0)</code>
/// </example>
public sealed partial class NativeTabBar : NativeComponent
{
    /// <summary>The tabs, in order. Nullable with no initializer so it stays a factory parameter.</summary>
    public IReadOnlyList<NativeTab>? Tabs { get; set; }

    /// <summary>
    ///     The selected tab index. Leave <c>null</c> (the default) to let the framework derive it from the
    ///     current route — the active tab then tracks navigation automatically. Set it to pin a specific tab.
    /// </summary>
    public int? Selected { get; set; }

    /// <summary>The tab bar's background color. Leave <c>null</c> to inherit the registered <c>NativeTheme</c>, else the platform default.</summary>
    public NativeColor? Background { get; set; }

    /// <summary>The tint of the selected tab (icon + label). <c>null</c> ⇒ theme, else platform default.</summary>
    public NativeColor? Tint { get; set; }

    /// <summary>The tint of the unselected tabs. <c>null</c> ⇒ theme, else platform default.</summary>
    public NativeColor? UnselectedTint { get; set; }
}
