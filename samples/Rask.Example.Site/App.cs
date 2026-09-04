using Rask.Core.Live;
using Rask.Ui;

namespace Rask.Example.Site;

/// <summary>
/// Root of the Rask landing site — a WASM app that renders the marketing page in pure Rask.
/// </summary>
/// <remarks>
/// <para>
/// This used to render the whole page itself, with no route anywhere in the project. That is why the
/// site could not be prerendered: the pass builds its plan from the registered route table, so an app
/// whose root component simply renders sections has nothing to enumerate and writes no pages — quietly,
/// because the guard that was supposed to notice looked for an <c>index.html</c>, which is what the boot
/// shell already is. The page moved to <see cref="Pages.HomePage" /> behind a <c>[Route("/")]</c> and
/// this root renders the router, exactly as the showcase's does.
/// </para>
/// <para>
/// Public + non-sealed to match the host's ActivatorUtilities + DAM contract.
/// </para>
/// </remarks>
public partial class App : Component
{
    protected override Component? HeadAssets =>
    [
        Title["Rask — the .NET One Person Framework"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Meta
            .Name("description")
            .Content("Rask is the .NET One Person Framework: one developer builds, runs, and ships a whole product — UI, data, auth, background work, and deploy — from one C# codebase on one SQLite-backed server. The same components run on Server and WebAssembly."),
        Meta.Name("theme-color").Content("#7c3aed"),
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/icon.svg"),

        // The kit's sheet FIRST, this page's second. The kit declares the --color-ui-* palette and the
        // page redefines the accent in its own @theme; an override only wins while it is the copy the
        // cascade reads last.
        //
        // Linked rather than inlined: the sheet is 36.8 KB gzipped and was being paid on every
        // document. The build writes it into wwwroot and the href carries its content hash, so it is
        // cached hard and busts exactly when the bytes change.
        Link.Rel("stylesheet").Href(UiStylesheet.Href(LiveOptions.PathBase)),
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
    ];

    /// <summary>
    ///     Turns the kit's theme on for the whole document.
    /// </summary>
    /// <remarks>
    ///     Load-bearing, not decorative — and its absence is what shipped this page to rask.sh with no
    ///     colour at all. The kit scopes daisyUI's theme to this attribute so that referencing the
    ///     package cannot repaint an app that only wanted a button, which means every <c>--color-ui-*</c>
    ///     token resolves to nothing until something in the ancestry carries it. The failure is silent:
    ///     structure and layout survive, colour does not, and nothing that only reads class names
    ///     notices. The showcase carries the same override for the same reason.
    /// </remarks>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir).Attributes((UiStylesheet.ThemeScopeAttribute, ""))[
            head,
            Body.Class(BodyClass)[body]
        ];

    protected override Component? Render() => Router;
}
