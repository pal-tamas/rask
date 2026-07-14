namespace Rask.Native.Components;

/// <summary>
///     A type-safe icon for native bar buttons and tabs. Each value pairs an iOS <b>SF Symbol</b> name with an
///     Android <b>drawable/Material</b> name — the platform head resolves its own token when it builds the bar.
///     Use a curated member (<c>NativeIcon.Home</c>) at the call site, or an escape hatch
///     (<see cref="Custom" /> / <see cref="SfSymbol" /> / <see cref="Drawable" />) for anything outside the set.
///     There is deliberately no implicit conversion from <c>string</c>: passing a raw <c>"plus"</c> must not
///     compile — that is the whole point of the type.
/// </summary>
/// <remarks>
///     Modelled as a <c>readonly record struct</c> (like <c>RouteUrl</c>) rather than an enum because an enum
///     cannot carry two per-platform tokens, nor be extended with a raw override. The curated members below are
///     the single, auditable icon-mapping table (mirroring the Rask.Bootstrap "every emitted string in one
///     place" convention); the tokens are already resolved, so the platform head does no lookup.
/// </remarks>
public readonly record struct NativeIcon
{
    /// <summary>Creates an icon from an explicit iOS SF Symbol name and Android drawable/Material name.</summary>
    public NativeIcon(string iosSymbol, string androidResource)
    {
        IosSymbol = iosSymbol;
        AndroidResource = androidResource;
    }

    /// <summary>The iOS SF Symbol name (fed to <c>UIImage.GetSystemImage</c> by the iOS head).</summary>
    public string IosSymbol { get; }

    /// <summary>The Android drawable / Material icon name (resolved to a drawable by the Android head).</summary>
    public string AndroidResource { get; }

    // --- Curated cross-platform vocabulary (the one auditable mapping table) ---------------------------------
    /// <summary>A home / house icon.</summary>
    public static NativeIcon Home => new("house", "ic_home");

    /// <summary>A person / profile icon.</summary>
    public static NativeIcon Person => new("person", "ic_person");

    /// <summary>An add / plus icon.</summary>
    public static NativeIcon Add => new("plus", "ic_add");

    /// <summary>A search / magnifier icon.</summary>
    public static NativeIcon Search => new("magnifyingglass", "ic_search");

    /// <summary>A settings / gear icon.</summary>
    public static NativeIcon Settings => new("gearshape", "ic_settings");

    /// <summary>A share icon.</summary>
    public static NativeIcon Share => new("square.and.arrow.up", "ic_share");

    /// <summary>A back / chevron-left icon.</summary>
    public static NativeIcon Back => new("chevron.backward", "ic_arrow_back");

    /// <summary>A list / menu icon.</summary>
    public static NativeIcon List => new("list.bullet", "ic_list");

    /// <summary>An info icon.</summary>
    public static NativeIcon Info => new("info.circle", "ic_info");

    /// <summary>A star / favourite icon.</summary>
    public static NativeIcon Star => new("star", "ic_star");

    /// <summary>An overflow / "more" (ellipsis) icon, for a <see cref="NativeMenuButton" />.</summary>
    public static NativeIcon More => new("ellipsis", "ic_more");

    // --- Escape hatches (named factories, like RouteUrl.External) --------------------------------------------
    /// <summary>An icon with explicit per-platform tokens, for icons outside the curated set.</summary>
    public static NativeIcon Custom(string iosSymbol, string androidResource) => new(iosSymbol, androidResource);

    /// <summary>An iOS-first raw SF Symbol name (reused as the Android name).</summary>
    public static NativeIcon SfSymbol(string name) => new(name, name);

    /// <summary>An Android-first raw drawable name (reused as the iOS name).</summary>
    public static NativeIcon Drawable(string name) => new(name, name);
}
