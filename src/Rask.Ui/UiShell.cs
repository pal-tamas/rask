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
            .Attributes((UiStylesheet.ThemeScopeAttribute, ""))[
            Div.Class("mx-auto w-full max-w-[110rem] sm:p-4 lg:p-6")[
                Div.Class("bg-base-100 sm:overflow-hidden sm:rounded-2xl sm:border sm:border-base-300")[
                    Children ?? []
                ]
            ]
        ];
}
