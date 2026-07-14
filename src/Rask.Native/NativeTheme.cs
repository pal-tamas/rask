using Rask.Native.Components;

namespace Rask.Native;

/// <summary>
///     Optional app-wide default appearance for native bars. Register one on <c>host.Services</c> (like
///     <see cref="INativeChrome" /> / <c>IShare</c>) to give every <c>NativeHeaderBar</c> / <c>NativeTabBar</c> /
///     <c>NativeToolbar</c> a consistent look without repeating colors on each bar. Resolution is layered: a
///     per-bar style prop wins; the theme fills the slots a bar left <c>null</c>; a slot unset in both keeps the
///     platform default. With no <see cref="NativeTheme" /> registered, bars use the platform default — so it is
///     fully backward compatible and opt-in.
/// </summary>
public sealed record NativeTheme
{
    /// <summary>Default background for every bar (header, tab bar, toolbar). <c>null</c> ⇒ platform default.</summary>
    public NativeColor? Background { get; init; }

    /// <summary>Default tint — bar buttons and the selected tab. <c>null</c> ⇒ platform default.</summary>
    public NativeColor? Tint { get; init; }

    /// <summary>Default header title-text color. <c>null</c> ⇒ platform default.</summary>
    public NativeColor? TitleColor { get; init; }

    /// <summary>Default tint for unselected tabs. <c>null</c> ⇒ platform default.</summary>
    public NativeColor? UnselectedTint { get; init; }
}
