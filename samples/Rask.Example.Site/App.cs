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
        // cascade reads last. Inlined rather than served because the kit ships no static assets — see
        // UiStylesheet.
        Style[Raw.Value(UiStylesheet.Css)],
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
    ];

    protected override Component? Render() => Router;
}
