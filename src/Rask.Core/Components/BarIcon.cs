namespace Rask.Core.Components;

/// <summary>
///     A type-safe icon for the cross-host chrome bars (<see cref="AppBar" />, <see cref="TabStrip" />). Each
///     value carries the token every host needs: a stable <see cref="Name" /> the web hosts emit as
///     <c>data-rask-icon</c>, an iOS <b>SF Symbol</b>, and an Android <b>drawable/Material</b> name.
/// </summary>
/// <remarks>
///     <para>
///         Use a curated member (<c>BarIcon.Home</c>) at the call site, or <see cref="Custom" /> for anything
///         outside the set. There is deliberately no implicit conversion from <c>string</c>: passing a raw
///         <c>"plus"</c> must not compile — that is the whole point of the type.
///     </para>
///     <para>
///         <b>This is the one auditable icon table.</b> <c>Rask.Native</c>'s <c>NativeIcon</c> — the escape
///         hatch for the native-only component family — derives its curated members from these, so the two
///         cannot drift.
///     </para>
///     <para>
///         The web hosts emit no glyph of their own: Core ships no SVG payload and takes no icon-font
///         dependency, so a bar button renders as <c>data-rask-icon="add"</c> and an app attaches whatever
///         icon set it already uses (Bootstrap Icons, Material, an SVG sprite) with one CSS rule per name.
///         The accessible name comes from the button's own title, not the glyph, so an unstyled page is still
///         usable.
///     </para>
/// </remarks>
public readonly record struct BarIcon
{
    /// <summary>Creates an icon from an explicit web name, iOS SF Symbol name, and Android drawable name.</summary>
    public BarIcon(string name, string iosSymbol, string androidResource)
    {
        Name = name;
        IosSymbol = iosSymbol;
        AndroidResource = androidResource;
    }

    /// <summary>The stable web token, emitted as <c>data-rask-icon</c> for CSS to hang a glyph on.</summary>
    public string Name { get; }

    /// <summary>The iOS SF Symbol name (fed to <c>UIImage.GetSystemImage</c> by the iOS head).</summary>
    public string IosSymbol { get; }

    /// <summary>The Android drawable / Material icon name (resolved to a drawable by the Android head).</summary>
    public string AndroidResource { get; }

    // --- Curated cross-platform vocabulary (the one auditable mapping table) ---------------------------------

    /// <summary>A home / house icon.</summary>
    public static BarIcon Home => new("home", "house", "ic_home");

    /// <summary>A person / profile icon.</summary>
    public static BarIcon Person => new("person", "person", "ic_person");

    /// <summary>An add / plus icon.</summary>
    public static BarIcon Add => new("add", "plus", "ic_add");

    /// <summary>A search / magnifier icon.</summary>
    public static BarIcon Search => new("search", "magnifyingglass", "ic_search");

    /// <summary>A settings / gear icon.</summary>
    public static BarIcon Settings => new("settings", "gearshape", "ic_settings");

    /// <summary>A share icon.</summary>
    public static BarIcon Share => new("share", "square.and.arrow.up", "ic_share");

    /// <summary>A back / chevron-left icon.</summary>
    public static BarIcon Back => new("back", "chevron.backward", "ic_arrow_back");

    /// <summary>A list / menu icon.</summary>
    public static BarIcon List => new("list", "list.bullet", "ic_list");

    /// <summary>An info icon.</summary>
    public static BarIcon Info => new("info", "info.circle", "ic_info");

    /// <summary>A star / favourite icon.</summary>
    public static BarIcon Star => new("star", "star", "ic_star");

    /// <summary>An overflow / "more" (ellipsis) icon.</summary>
    public static BarIcon More => new("more", "ellipsis", "ic_more");

    // --- Escape hatch ----------------------------------------------------------------------------------------

    /// <summary>An icon with explicit per-host tokens, for icons outside the curated set.</summary>
    public static BarIcon Custom(string name, string iosSymbol, string androidResource) =>
        new(name, iosSymbol, androidResource);
}
