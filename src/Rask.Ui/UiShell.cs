namespace Rask.Ui;

/// <summary>
/// The surface's frame: a well, and the panel the surface is drawn on.
/// </summary>
/// <remarks>
/// Full-bleed on a phone and a bordered card from <c>sm</c> up. That is the whole responsive story for the
/// outer shell — on a 360px screen the margins and the rounded corner cost about 8% of the usable width and
/// buy nothing, so below <c>sm</c> the console simply IS the page.
/// </remarks>
public sealed partial class UiShell : Component
{
    /// <summary>
    /// The daisyUI theme this shell's subtree is painted with. <see langword="null" /> — the default —
    /// follows the operating system: light, and dark under <c>prefers-color-scheme: dark</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// daisyUI names a theme with <c>data-theme</c> <b>on the element carrying the theme scope</b>, and
    /// this shell IS that element (see <see cref="UiStylesheet.ThemeScopeAttribute" />). Without this
    /// property a surface built on the shell had no way to name one at all: writing
    /// <c>data-theme</c> on an ancestor does not help, because the rule that follows the OS is
    /// <c>[data-rask-ui]:not([data-theme])</c> and it matches this div, redefining <c>--color-base-*</c>
    /// for everything inside it.
    /// </para>
    /// <para>
    /// Which is not a theoretical gap: a surface that pins its own palette in its own stylesheet and
    /// leaves the shell unpinned renders its chrome dark and its content light on an operator whose
    /// laptop is in dark mode, with every class name in the markup correct.
    /// </para>
    /// </remarks>
    public UiThemeName? Theme { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // .rask-ops is a hook, not a fence. It was a fence while these pages rendered inside the host
        // application's document and every rule had to be scoped under it; the console is now a mounted
        // application with its own document (RaskMountedApp), so the stylesheet is free to be ordinary.
        // The shell carries the kit's theme scope so that a surface built on it does not have to remember
        // to. Without the attribute the palette is not defined at all — daisyUI's colours are confined to
        // it precisely so that referencing this package cannot repaint an application that only wanted a
        // button — and every component inside would render structurally correct with no colour, which no
        // markup assertion can see. A surface that does NOT use this shell opts in itself, on <html> or on
        // a container; see UiStylesheet.ThemeScopeAttribute.
        Div
            .Class("rask-ops min-h-screen bg-base-200 text-base-content")
            .Attributes(ScopeAttributes())[
            Div.Class("mx-auto w-full max-w-[110rem] sm:p-4 lg:p-6")[
                Div.Class("bg-base-100 sm:overflow-hidden sm:rounded-2xl sm:border sm:border-base-300")[
                    Children ?? []
                ]
            ]
        ];

    // Built rather than written inline because the theme is optional and an empty data-theme is NOT the
    // same as no data-theme: `[data-rask-ui]:not([data-theme])` is what makes an unpinned shell follow the
    // OS, and an attribute present with an unmatched value would leave the subtree with no palette at all.
    private (string, string?)[] ScopeAttributes() =>
        Theme is { } theme
            ? [(UiStylesheet.ThemeScopeAttribute, ""), ("data-theme", UiTheme.Value(theme))]
            : [(UiStylesheet.ThemeScopeAttribute, "")];
}
