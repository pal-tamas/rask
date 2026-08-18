using Rask.Chrome.Components;

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
///     cannot carry two per-platform tokens, nor be extended with a raw override; the tokens are already
///     resolved, so the platform head does no lookup.
///     <para>
///         The curated members below <b>delegate to <see cref="BarIcon" /></b> (Rask.Core), which is the one
///         auditable icon-mapping table. Two hand-maintained copies of the same SF-Symbol/drawable pairs would
///         drift the moment either gained an icon, and the portable bars and the native-only ones would then
///         disagree about what <c>Home</c> looks like on the same screen.
///     </para>
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
    public static NativeIcon Home => From(BarIcon.Home);

    /// <summary>A person / profile icon.</summary>
    public static NativeIcon Person => From(BarIcon.Person);

    /// <summary>An add / plus icon.</summary>
    public static NativeIcon Add => From(BarIcon.Add);

    /// <summary>A search / magnifier icon.</summary>
    public static NativeIcon Search => From(BarIcon.Search);

    /// <summary>A settings / gear icon.</summary>
    public static NativeIcon Settings => From(BarIcon.Settings);

    /// <summary>A share icon.</summary>
    public static NativeIcon Share => From(BarIcon.Share);

    /// <summary>A back / chevron-left icon.</summary>
    public static NativeIcon Back => From(BarIcon.Back);

    /// <summary>A list / menu icon.</summary>
    public static NativeIcon List => From(BarIcon.List);

    /// <summary>An info icon.</summary>
    public static NativeIcon Info => From(BarIcon.Info);

    /// <summary>A star / favourite icon.</summary>
    public static NativeIcon Star => From(BarIcon.Star);

    /// <summary>An overflow / "more" (ellipsis) icon, for a <see cref="NativeMenuButton" />.</summary>
    public static NativeIcon More => From(BarIcon.More);

    /// <summary>
    ///     The native tokens of a portable <see cref="BarIcon" />. This is how the curated members above stay
    ///     a view onto Core's single table rather than a second copy of it.
    /// </summary>
    public static NativeIcon From(BarIcon icon) => new(icon.IosSymbol, icon.AndroidResource);

    // --- Escape hatches (named factories, like RouteUrl.External) --------------------------------------------
    /// <summary>An icon with explicit per-platform tokens, for icons outside the curated set.</summary>
    public static NativeIcon Custom(string iosSymbol, string androidResource) => new(iosSymbol, androidResource);

    /// <summary>An iOS-first raw SF Symbol name (reused as the Android name).</summary>
    public static NativeIcon SfSymbol(string name) => new(name, name);

    /// <summary>An Android-first raw drawable name (reused as the iOS name).</summary>
    public static NativeIcon Drawable(string name) => new(name, name);
}
