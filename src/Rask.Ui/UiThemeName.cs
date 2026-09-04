namespace Rask.Ui;

/// <summary>
/// Every theme the kit ships, named as daisyUI names them.
/// </summary>
/// <remarks>
/// <para>
/// The kit enables daisyUI's whole theme set rather than a curated slice of it, and this is what makes
/// choosing one a compile-time act instead of a string literal. A typo in <c>data-theme="cupacke"</c>
/// is not an error anywhere — the attribute is simply unmatched and the palette silently stays put,
/// which is the same class of silent failure the theme scope already causes when it is missing.
/// </para>
/// <para>
/// Costed before it was taken: the whole set is <b>+6.2 KB gzipped</b> over the two the kit used to
/// ship (30.6 KB → 36.8 KB), because a theme is a block of custom properties rather than a second copy
/// of the component rules. Thirty-three extra palettes for that is a trade worth making once, centrally,
/// rather than asking every app to pick.
/// </para>
/// <para>
/// <see cref="Light" /> is the default and <see cref="Dark" /> follows the operating system. Naming any
/// other theme means saying so — with <see cref="UiThemePicker" />, or with an explicit
/// <c>data-theme</c> on the element carrying the theme scope.
/// </para>
/// </remarks>
public enum UiThemeName
{
    /// <summary>daisyUI's default light palette. What a surface gets when it asks for nothing.</summary>
    Light = 0,

    /// <summary>daisyUI's default dark palette. Applied by <c>prefers-color-scheme</c> when no theme is named.</summary>
    Dark,

    Abyss,
    Acid,
    Aqua,
    Autumn,
    Black,
    Bumblebee,
    Business,
    Caramellatte,
    Cmyk,
    Coffee,
    Corporate,
    Cupcake,
    Cyberpunk,
    Dim,
    Dracula,
    Emerald,
    Fantasy,
    Forest,
    Garden,
    Halloween,
    Lemonade,
    Lofi,
    Luxury,
    Night,
    Nord,
    Pastel,
    Retro,
    Silk,
    Sunset,
    Synthwave,
    Valentine,
    Winter,

    /// <summary>Borders and type only — daisyUI's unstyled wireframe palette.</summary>
    Wireframe,
}

/// <summary>
/// Turns a <see cref="UiThemeName" /> into the string daisyUI matches on.
/// </summary>
public static class UiTheme
{
    /// <summary>
    /// The <c>data-theme</c> value for <paramref name="theme" /> — its name, lowercased.
    /// </summary>
    /// <remarks>
    /// daisyUI's own names are all lowercase single words, so the mapping is mechanical rather than a
    /// table that could drift out of step with the enum. <c>UiThemeTests</c> holds every member to a
    /// theme the compiled stylesheet actually defines, so a member added here without the sheet
    /// shipping it fails the build's tests rather than a page.
    /// </remarks>
    public static string Value(UiThemeName theme) =>
        theme.ToString().ToLowerInvariant();

    /// <summary>Every theme the kit ships, in declaration order.</summary>
    public static IReadOnlyList<UiThemeName> All { get; } = Enum.GetValues<UiThemeName>();
}
