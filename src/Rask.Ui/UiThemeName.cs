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

    /// <summary>
    /// Follow the operating system's light/dark preference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT a palette, and the distinction is load-bearing: this member means the ABSENCE of a choice.
    /// daisyUI compiles the default colours under <c>:where([data-rask-ui])</c> and, inside
    /// <c>@media (prefers-color-scheme: dark)</c>, under <c>[data-rask-ui]:not([data-theme])</c> — so a
    /// scope with no <c>data-theme</c> already follows the reader's machine. Selecting this means
    /// REMOVING the attribute, never stamping <c>data-theme="system"</c>, which matches nothing and
    /// would leave every colour on the page undefined.
    /// </para>
    /// <para>
    /// It is excluded from <see cref="UiTheme.All" /> for that reason — that list is the palettes the
    /// stylesheet carries — and <see cref="UiThemePicker" /> offers it separately, first, because a
    /// reader who has pinned a theme otherwise has no way back to their own preference.
    /// </para>
    /// </remarks>
    System,
}
